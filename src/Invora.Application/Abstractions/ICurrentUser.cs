namespace Invora.Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    IReadOnlySet<Guid> AuthorizedBranchIds { get; }
    IReadOnlySet<string> Permissions { get; }
}
