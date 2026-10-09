using System.Text.Json;
using Invora.Contracts.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Invora.Application.Abstractions;
using Invora.Application.Common;
using Invora.Contracts.Identity;
using Invora.Domain.Common;
using Invora.Domain.Modules.Businesses;
using Invora.Domain.Modules.Identity;
using Invora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace Invora.Infrastructure.Identity;

public sealed class IdentityService(InvoraDbContext db, IPasswordHasher<StaffUser> hasher, IOptions<AuthOptions> options) : IIdentityService
{
    private readonly AuthOptions auth = options.Value;
    private static string Normalize(string login) => login.Trim().ToUpperInvariant();
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static void CheckPassword(string password)
    {
        if (password.Length is < 12 or > 128 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new DomainException("VALIDATION_FAILED", "Password requires 12–128 characters including a letter and number.");
    }
    private static void CheckLogin(string login, string name)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length > 100 || login.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new DomainException("VALIDATION_FAILED", "Login and display name are required and must fit their limits.");
    }
    // Serialize identity changes and refresh rotation across processes in this single-business database.
    private Task LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72619431)", ct);
    private void Audit(Guid? actor, string action, Guid resource, object? details = null) => db.AccessAudits.Add(new() { ActorId = actor, Action = action, ResourceId = resource, DetailsJson = JsonSerializer.Serialize(details ?? new { }) });
    private async Task<UserView> ViewAsync(StaffUser user, CancellationToken ct) => new(user.Id, user.Login, user.DisplayName, user.IsOwner, user.IsActive, user.Permissions, await db.StaffBranches.Where(x => x.UserId == user.Id).Select(x => x.BranchId).ToArrayAsync(ct));
    private async Task<SessionResult> IssueAsync(StaffUser user, AuthSession session, CancellationToken ct)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        db.RefreshCredentials.Add(new() { SessionId = session.Id, TokenHash = Hash(raw) });
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var jwt = new JwtSecurityToken(auth.Issuer, auth.Audience, [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim("sid", session.Id.ToString())], expires: expiry.UtcDateTime, signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)), SecurityAlgorithms.HmacSha256));
        await db.SaveChangesAsync(ct);
        return new(new(new JwtSecurityTokenHandler().WriteToken(jwt), expiry, await ViewAsync(user, ct)), raw);
    }
    public async Task<SessionResult> SetupAsync(SetupRequest request, CancellationToken ct)
    {
        CheckLogin(request.OwnerLogin, request.OwnerName); CheckPassword(request.Password);
        if (string.IsNullOrWhiteSpace(request.TradeName) || request.TradeName.Length > 200 || string.IsNullOrWhiteSpace(request.BranchCode) || request.BranchCode.Length > 20 || string.IsNullOrWhiteSpace(request.BranchName) || request.BranchName.Length > 200)
            throw new DomainException("VALIDATION_FAILED", "Business and branch details are required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        if (await db.Businesses.AnyAsync(ct)) throw new DomainException("SETUP_COMPLETE", "Business setup is already complete.");
        var business = BusinessProfile.Create(request.TradeName); var branch = Branch.Create(business.Id, request.BranchCode, request.BranchName);
        var owner = new StaffUser { Login = Normalize(request.OwnerLogin), DisplayName = request.OwnerName.Trim(), IsOwner = true, Permissions = PermissionCodes.All.ToArray() };
        owner.PasswordHash = hasher.HashPassword(owner, request.Password);
        db.Businesses.Add(business); db.Branches.Add(branch); db.StaffUsers.Add(owner); db.StaffBranches.Add(new() { UserId = owner.Id, BranchId = branch.Id });
        var session = new AuthSession { UserId = owner.Id, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14) }; db.AuthSessions.Add(session);
        Audit(owner.Id, "BUSINESS_SETUP", business.Id);
        var result = await IssueAsync(owner, session, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<SessionResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var user = await db.StaffUsers.SingleOrDefaultAsync(x => x.Login == Normalize(request.Login), ct);
        if (user is null || !user.IsActive || user.LockedUntilUtc > DateTimeOffset.UtcNow) throw new DomainException("INVALID_CREDENTIALS", "Invalid credentials or account unavailable.");
        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++; if (user.FailedAttempts >= 5) { user.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(15); user.FailedAttempts = 0; }
            Audit(user.Id, "LOGIN_FAILED", user.Id); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            throw new DomainException("INVALID_CREDENTIALS", "Invalid credentials or account unavailable.");
        }
        if (verification == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.FailedAttempts = 0; user.LockedUntilUtc = null;
        var session = new AuthSession { UserId = user.Id, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14) }; db.AuthSessions.Add(session);
        Audit(user.Id, "LOGIN", session.Id); var result = await IssueAsync(user, session, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<SessionResult> RefreshAsync(string token, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var credential = await db.RefreshCredentials.SingleOrDefaultAsync(x => x.TokenHash == Hash(token), ct);
        if (credential is null) throw new DomainException("INVALID_SESSION", "Session unavailable.");
        var session = await db.AuthSessions.SingleAsync(x => x.Id == credential.SessionId, ct);
        if (credential.UsedAtUtc is not null)
        {
            session.RevokedAtUtc = DateTimeOffset.UtcNow; Audit(session.UserId, "REFRESH_REPLAY", session.Id); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            throw new DomainException("INVALID_SESSION", "Session unavailable.");
        }
        var user = await db.StaffUsers.SingleAsync(x => x.Id == session.UserId, ct);
        if (session.RevokedAtUtc is not null || session.ExpiresAtUtc <= DateTimeOffset.UtcNow || !user.IsActive) throw new DomainException("INVALID_SESSION", "Session unavailable.");
        credential.UsedAtUtc = DateTimeOffset.UtcNow; var result = await IssueAsync(user, session, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task ChangePasswordAsync(Guid actor, ChangePasswordRequest request, CancellationToken ct)
    {
        CheckPassword(request.NewPassword);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var user = await db.StaffUsers.SingleAsync(x => x.Id == actor && x.IsActive, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new DomainException("INVALID_CREDENTIALS", "Current password is incorrect.");
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        foreach (var session in await db.AuthSessions.Where(x => x.UserId == actor && x.RevokedAtUtc == null).ToListAsync(ct)) session.RevokedAtUtc = DateTimeOffset.UtcNow;
        Audit(actor, "PASSWORD_CHANGED", actor); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var credential = await db.RefreshCredentials.SingleOrDefaultAsync(x => x.TokenHash == Hash(token), ct);
        if (credential is not null) { var session = await db.AuthSessions.SingleAsync(x => x.Id == credential.SessionId, ct); session.RevokedAtUtc = DateTimeOffset.UtcNow; Audit(session.UserId, "LOGOUT", session.Id); await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct);
    }
    public async Task<UserView?> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (!await db.AuthSessions.AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow, ct)) return null;
        var user = await db.StaffUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        return user is null ? null : await ViewAsync(user, ct);
    }
    private async Task ValidateGrantsAsync(Guid actor, string[] permissions, Guid[] branches, CancellationToken ct)
    {
        var manager = await db.StaffUsers.SingleAsync(x => x.Id == actor && x.IsActive, ct);
        if (!manager.Permissions.Contains("users.manage")) throw new DomainException("ACCESS_DENIED", "Staff management permission is required.");
        if (permissions.Any(x => !PermissionCodes.All.Contains(x) || !manager.Permissions.Contains(x))) throw new DomainException("ACCESS_DENIED", "Cannot grant permissions outside your own access.");
        var allowed = await db.StaffBranches.Where(x => x.UserId == actor).Select(x => x.BranchId).ToArrayAsync(ct);
        if (branches.Length == 0 || branches.Any(x => !allowed.Contains(x)) || await db.Branches.CountAsync(x => branches.Contains(x.Id) && x.IsActive, ct) != branches.Distinct().Count()) throw new DomainException("ACCESS_DENIED", "Branch grants must be active and within your access.");
    }
    public async Task<UserView> CreateStaffAsync(Guid actor, StaffRequest request, CancellationToken ct)
    {
        CheckLogin(request.Login, request.DisplayName); CheckPassword(request.Password);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct); await ValidateGrantsAsync(actor, request.Permissions, request.BranchIds, ct);
        if (await db.StaffUsers.AnyAsync(x => x.Login == Normalize(request.Login), ct)) throw new DomainException("LOGIN_EXISTS", "Login already exists.");
        var user = new StaffUser { Login = Normalize(request.Login), DisplayName = request.DisplayName.Trim(), Permissions = request.Permissions.Distinct().ToArray() }; user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.StaffUsers.Add(user); foreach (var id in request.BranchIds.Distinct()) db.StaffBranches.Add(new() { UserId = user.Id, BranchId = id }); Audit(actor, "STAFF_CREATED", user.Id, new { user.Login, user.DisplayName, user.Permissions, BranchIds = request.BranchIds.Distinct().ToArray() });
        await db.SaveChangesAsync(ct); var result = await ViewAsync(user, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<UserView> UpdateAccessAsync(Guid actor, Guid userId, AccessRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct); await ValidateGrantsAsync(actor, request.Permissions, request.BranchIds, ct);
        var user = await db.StaffUsers.SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new DomainException("NOT_FOUND", "Staff member not found.");
        var manager = await db.StaffUsers.SingleAsync(x => x.Id == actor, ct);
        var currentBranches = await db.StaffBranches.Where(x => x.UserId == userId).ToListAsync(ct);
        var actorBranches = await db.StaffBranches.Where(x => x.UserId == actor).Select(x => x.BranchId).ToArrayAsync(ct);
        if (!manager.IsOwner && (user.IsOwner || currentBranches.Any(x => !actorBranches.Contains(x.BranchId)) || user.Permissions.Any(x => !manager.Permissions.Contains(x)))) throw new DomainException("ACCESS_DENIED", "Cannot manage staff outside your access.");
        if (user.IsOwner) throw new DomainException("OWNER_PROTECTED", "Owner access cannot be removed through staff management.");
        var before = new { user.Permissions, user.IsActive, BranchIds = currentBranches.Select(x => x.BranchId).ToArray() };
        user.Permissions = request.Permissions.Distinct().ToArray(); user.IsActive = request.IsActive;
        db.StaffBranches.RemoveRange(currentBranches.Where(x => !request.BranchIds.Contains(x.BranchId)));
        foreach (var id in request.BranchIds.Distinct().Where(id => currentBranches.All(x => x.BranchId != id))) db.StaffBranches.Add(new() { UserId = userId, BranchId = id });
        foreach (var session in await db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(ct)) session.RevokedAtUtc = DateTimeOffset.UtcNow;
        Audit(actor, "STAFF_ACCESS_CHANGED", userId, new { Before = before, After = new { user.Permissions, user.IsActive, BranchIds = request.BranchIds.Distinct().ToArray() } }); await db.SaveChangesAsync(ct); var result = await ViewAsync(user, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<PageResponse<UserView>> ListUsersAsync(Guid actor, int page, int pageSize, CancellationToken ct)
    {
        var manager = await db.StaffUsers.AsNoTracking().SingleAsync(x => x.Id == actor && x.IsActive, ct);
        var allowed = await db.StaffBranches.Where(x => x.UserId == actor).Select(x => x.BranchId).ToArrayAsync(ct);
        var query = db.StaffUsers.AsNoTracking();
        if (!manager.IsOwner) query = query.Where(x => !x.IsOwner && !db.StaffBranches.Any(b => b.UserId == x.Id && !allowed.Contains(b.BranchId)));
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100) throw new DomainException("VALIDATION_FAILED", "Page must be positive and page size between 1 and 100.");
        var count = await query.LongCountAsync(ct);
        var users = await query.OrderBy(x => x.Login).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var result = new List<UserView>(); foreach (var user in users) result.Add(await ViewAsync(user, ct)); return new(result, page, pageSize, count);
    }
    public async Task<BusinessView> GetBusinessAsync(CancellationToken ct)
    {
        var b = await db.Businesses.AsNoTracking().SingleAsync(ct); return new(b.Id, b.TradeName, b.Currency, b.TimeZone, b.FinancialYearStartMonth);
    }
    public async Task<BusinessView> UpdateBusinessAsync(Guid actor, ProfileRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        if (!await db.StaffUsers.AnyAsync(x => x.Id == actor && x.IsActive && x.Permissions.Contains("business.settings"), ct)) throw new DomainException("ACCESS_DENIED", "Business settings permission is required.");
        var b = await db.Businesses.SingleAsync(ct);
        var before = new { b.TradeName, b.TimeZone, b.FinancialYearStartMonth };
        if(request.FinancialYearStartMonth!=b.FinancialYearStartMonth && (await db.Set<Invora.Domain.Modules.Retail.Purchase>().AnyAsync(ct) || await db.Set<Invora.Domain.Modules.Retail.Sale>().AnyAsync(ct)))throw new DomainException("VALIDATION_FAILED","Financial year start is fixed after the first purchase or sales draft.");
        b.Update(request.TradeName, request.TimeZone, request.FinancialYearStartMonth);
        Audit(actor, "BUSINESS_UPDATED", b.Id, new { Before = before, After = new { b.TradeName, b.TimeZone, b.FinancialYearStartMonth } }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await GetBusinessAsync(ct);
    }
    public async Task<IReadOnlyList<BranchView>> ListBranchesAsync(Guid userId, CancellationToken ct)
    {
        var ids = db.StaffBranches.Where(x => x.UserId == userId).Select(x => x.BranchId);
        return await db.Branches.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).OrderBy(x => x.Code).Select(x => new BranchView(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(ct);
    }
    public async Task<BranchView> CreateBranchAsync(Guid actor, BranchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 20 || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200) throw new DomainException("VALIDATION_FAILED", "Branch details are invalid.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        if (!await db.StaffUsers.AnyAsync(x => x.Id == actor && x.IsActive && x.Permissions.Contains("branches.manage"), ct)) throw new DomainException("ACCESS_DENIED", "Branch management permission is required.");
        var b = await db.Businesses.SingleAsync(ct); if (await db.Branches.AnyAsync(x => x.Code == request.Code.Trim().ToUpperInvariant(), ct)) throw new DomainException("BRANCH_EXISTS", "Branch code already exists.");
        var branch = Branch.Create(b.Id, request.Code, request.Name); db.Branches.Add(branch); db.StaffBranches.Add(new() { UserId = actor, BranchId = branch.Id });
        foreach (var owner in await db.StaffUsers.Where(x => x.IsOwner && x.Id != actor && x.IsActive).ToListAsync(ct)) db.StaffBranches.Add(new() { UserId = owner.Id, BranchId = branch.Id });
        Audit(actor, "BRANCH_CREATED", branch.Id); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(branch.Id, branch.Code, branch.Name, branch.IsActive);
    }
}
