using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Invora.Application.Abstractions;
using Invora.Application.Common;
using Invora.Application.Modules.Identity;
using Invora.Contracts.Identity;
using Invora.Domain.Modules.Identity;
using Invora.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
namespace Invora.Api.Authorization;

public static class AuthRegistration
{
    public static void AddInvoraIdentity(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Auth"); var options = config.Get<AuthOptions>() ?? new();
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32 || !Uri.TryCreate(options.BrowserOrigin, UriKind.Absolute, out var origin) || (origin.Scheme != "https" && !(builder.Environment.IsDevelopment() && origin.Scheme == "http")))
            throw new InvalidOperationException("Auth:SigningKey requires 32+ bytes and Auth:BrowserOrigin requires an HTTPS origin (HTTP allowed only in Development).");
        builder.Services.Configure<AuthOptions>(config);
        builder.Services.Configure<PasswordHasherOptions>(x => x.IterationCount = 210000);
        builder.Services.AddScoped<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>(); builder.Services.AddScoped<IIdentityService, IdentityService>();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordValidator>();
        builder.Services.AddScoped<IValidator<SetupRequest>, SetupValidator>(); builder.Services.AddScoped<IValidator<LoginRequest>, LoginValidator>(); builder.Services.AddScoped<IValidator<StaffRequest>, StaffValidator>(); builder.Services.AddScoped<IValidator<AccessRequest>, AccessValidator>(); builder.Services.AddScoped<IValidator<BranchRequest>, BranchValidator>(); builder.Services.AddScoped<IValidator<ProfileRequest>, ProfileValidator>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt =>
        {
            jwt.MapInboundClaims = false; jwt.TokenValidationParameters = new() { ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true, ValidIssuer = options.Issuer, ValidAudience = options.Audience, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), ClockSkew = TimeSpan.FromSeconds(10), ValidAlgorithms = [SecurityAlgorithms.HmacSha256] };
            jwt.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    if (!Guid.TryParse(ctx.Principal?.FindFirstValue("sub"), out var uid) || !Guid.TryParse(ctx.Principal?.FindFirstValue("sid"), out var sid)) { ctx.Fail("Invalid session."); return; }
                    var user = await ctx.HttpContext.RequestServices.GetRequiredService<IIdentityService>().ValidateSessionAsync(uid, sid, ctx.HttpContext.RequestAborted);
                    if (user is null) { ctx.Fail("Session unavailable."); return; }
                    var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
                    foreach (var claim in identity.FindAll("permission").Concat(identity.FindAll("branch")).ToArray()) identity.RemoveClaim(claim);
                    foreach (var p in user.Permissions) identity.AddClaim(new("permission", p)); foreach (var b in user.BranchIds) identity.AddClaim(new("branch", b.ToString()));
                    ctx.HttpContext.Items["current-user"] = user;
                }
            };
        });
        builder.Services.AddAuthorization(o => { foreach (var permission in PermissionCodes.All) o.AddPolicy(permission, p => p.RequireAuthenticatedUser().RequireClaim("permission", permission)); });
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("session", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }
}
