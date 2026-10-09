using Invora.Contracts.Common;
using Invora.Contracts.Identity;
namespace Invora.Application.Abstractions;

public interface IIdentityService
{
    Task<SessionResult> SetupAsync(SetupRequest request, CancellationToken ct);
    Task<SessionResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<SessionResult> RefreshAsync(string token, CancellationToken ct);
    Task ChangePasswordAsync(Guid actor, ChangePasswordRequest request, CancellationToken ct);
    Task LogoutAsync(string token, CancellationToken ct);
    Task<UserView?> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<UserView> CreateStaffAsync(Guid actor, StaffRequest request, CancellationToken ct);
    Task<UserView> UpdateAccessAsync(Guid actor, Guid userId, AccessRequest request, CancellationToken ct);
    Task<PageResponse<UserView>> ListUsersAsync(Guid actor, int page, int pageSize, CancellationToken ct);
    Task<BusinessView> GetBusinessAsync(CancellationToken ct);
    Task<BusinessView> UpdateBusinessAsync(Guid actor, ProfileRequest request, CancellationToken ct);
    Task<IReadOnlyList<BranchView>> ListBranchesAsync(Guid userId, CancellationToken ct);
    Task<BranchView> CreateBranchAsync(Guid actor, BranchRequest request, CancellationToken ct);
}
