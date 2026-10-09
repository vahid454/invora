using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Invora.Application.Abstractions;
using Invora.Application.Common;
using Invora.Contracts.Identity;
using Invora.Infrastructure.Identity;
using Microsoft.Extensions.Options;
namespace Invora.Api.Modules.Identity;

public static class IdentityEndpoints
{
    private const string CookieName = "invora.refresh";
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue("sub")!);
    private static IResult BrowserGuard(HttpContext ctx, AuthOptions options)
    {
        if (ctx.Request.Headers.Origin.ToString() != options.BrowserOrigin || ctx.Request.Headers["X-Invora-CSRF"] != "1")
            return Results.Problem(statusCode: 403, title: "Browser origin or CSRF header rejected.");
        return Results.NoContent();
    }
    private static CookieOptions Cookie(IHostEnvironment env) => new() { HttpOnly = true, Secure = !env.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = "/api/v1/auth", MaxAge = TimeSpan.FromDays(14) };
    private static IResult Session(HttpContext ctx, SessionResult result, IHostEnvironment env)
    {
        ctx.Response.Cookies.Append(CookieName, result.RefreshToken, Cookie(env)); ctx.Response.Headers.CacheControl = "no-store"; return Results.Ok(result.Response);
    }
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").AddEndpointFilter<ValidationFilter>();
        auth.MapPost("/register", async (SetupRequest request, HttpContext ctx, IIdentityService service, IOptions<AuthOptions> options, IHostEnvironment env, CancellationToken ct) =>
        {
            var supplied = ctx.Request.Headers["X-Invora-Bootstrap"].ToString(); var expected = options.Value.BootstrapKey;
            if (expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)))) return Results.Problem(statusCode: 403, title: "Setup is not authorized.");
            return Session(ctx, await service.SetupAsync(request, ct), env);
        }).RequireRateLimiting("auth");
        auth.MapPost("/login", async (LoginRequest request, HttpContext ctx, IIdentityService service, IHostEnvironment env, CancellationToken ct) => Session(ctx, await service.LoginAsync(request, ct), env)).RequireRateLimiting("auth");
        auth.MapPost("/refresh", async (HttpContext ctx, IIdentityService service, IOptions<AuthOptions> options, IHostEnvironment env, CancellationToken ct) =>
        {
            if (BrowserGuard(ctx, options.Value) is not Microsoft.AspNetCore.Http.HttpResults.NoContent) return BrowserGuard(ctx, options.Value);
            if (!ctx.Request.Cookies.TryGetValue(CookieName, out var token)) return Results.Unauthorized();
            return Session(ctx, await service.RefreshAsync(token, ct), env);
        }).RequireRateLimiting("session");
        auth.MapPost("/logout", async (HttpContext ctx, IIdentityService service, IOptions<AuthOptions> options, IHostEnvironment env, CancellationToken ct) =>
        {
            if (BrowserGuard(ctx, options.Value) is not Microsoft.AspNetCore.Http.HttpResults.NoContent) return BrowserGuard(ctx, options.Value);
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var token)) await service.LogoutAsync(token, ct);
            ctx.Response.Cookies.Delete(CookieName, Cookie(env)); return Results.NoContent();
        }).RequireRateLimiting("session");
        auth.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext ctx, IIdentityService service, IHostEnvironment env, CancellationToken ct) =>
        {
            await service.ChangePasswordAsync(Actor(ctx), request, ct);
            ctx.Response.Cookies.Delete(CookieName, Cookie(env)); return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("auth");
        auth.MapGet("/me", (HttpContext ctx) => Results.Ok(ctx.Items["current-user"])).RequireAuthorization();
        var business = app.MapGroup("/api/v1/business").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        business.MapGet("/", async (IIdentityService service, CancellationToken ct) => Results.Ok(await service.GetBusinessAsync(ct)));
        business.MapPut("/", async (ProfileRequest request, HttpContext ctx, IIdentityService service, CancellationToken ct) => Results.Ok(await service.UpdateBusinessAsync(Actor(ctx), request, ct))).RequireAuthorization("business.settings");
        var branches = app.MapGroup("/api/v1/branches").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        branches.MapGet("/", async (HttpContext ctx, IIdentityService service, CancellationToken ct) => Results.Ok(await service.ListBranchesAsync(Actor(ctx), ct)));
        branches.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, IIdentityService service, CancellationToken ct) =>
        {
            var branch = (await service.ListBranchesAsync(Actor(ctx), ct)).SingleOrDefault(x => x.Id == id); return branch is null ? Results.NotFound() : Results.Ok(branch);
        });
        branches.MapPost("/", async (BranchRequest request, HttpContext ctx, IIdentityService service, CancellationToken ct) => Results.Ok(await service.CreateBranchAsync(Actor(ctx), request, ct))).RequireAuthorization("branches.manage");
        app.MapGet("/api/v1/role-templates", (HttpContext ctx)=>Results.Ok(new[]{new{name="Co-owner / store manager",permissions=PermissionCodes.All},new{name="Trusted counter staff",permissions=new[]{"inventory.view","inventory.cost.view","inventory.create","inventory.adjust","sales.view","sales.create","sales.discount","sales.return","sales.cancel","customers.view","customers.manage","customers.credit.view","payments.view","payments.create","payments.reverse","ledger.adjust","purchase.view","purchase.create","supplier.view","supplier.manage","expenses.view","expenses.create","reports.view"}},new{name="Cashier",permissions=new[]{"inventory.view","sales.view","sales.create","customers.view","customers.manage","payments.view","payments.create"}},new{name="Inventory manager",permissions=new[]{"inventory.view","inventory.cost.view","inventory.create","inventory.adjust","inventory.transfer","purchase.view","purchase.create","supplier.view","supplier.manage","files.upload"}},new{name="Accountant",permissions=new[]{"inventory.view","inventory.cost.view","sales.view","customers.view","customers.credit.view","payments.view","payments.create","payments.reverse","purchase.view","supplier.view","expenses.view","expenses.create","expenses.reverse","reports.view","reports.export","profit.view","files.upload"}}})).RequireAuthorization("users.manage");
        var users = app.MapGroup("/api/v1/users").RequireAuthorization("users.manage").AddEndpointFilter<ValidationFilter>();
        users.MapGet("/", async (HttpContext ctx, IIdentityService service, CancellationToken ct, int page = 1, int pageSize = 25) => Results.Ok(await service.ListUsersAsync(Actor(ctx), page, pageSize, ct)));
        users.MapPost("/", async (StaffRequest request, HttpContext ctx, IIdentityService service, CancellationToken ct) => Results.Ok(await service.CreateStaffAsync(Actor(ctx), request, ct)));
        users.MapPut("/{id:guid}/access", async (Guid id, AccessRequest request, HttpContext ctx, IIdentityService service, CancellationToken ct) => Results.Ok(await service.UpdateAccessAsync(Actor(ctx), id, request, ct)));
        app.MapGet("/api/v1/permissions", () => Results.Ok(PermissionCodes.All)).RequireAuthorization("users.manage");
    }
}
