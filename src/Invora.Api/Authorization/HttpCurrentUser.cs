using System.Security.Claims;
using Invora.Application.Abstractions;
namespace Invora.Api.Authorization;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? throw new InvalidOperationException("No authenticated request context.");
    public Guid UserId => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : throw new InvalidOperationException("No authenticated user.");
    public IReadOnlySet<Guid> AuthorizedBranchIds => User.FindAll("branch").Select(x => Guid.Parse(x.Value)).ToHashSet();
    public IReadOnlySet<string> Permissions => User.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
}
