using Invora.Domain.Common;
namespace Invora.Domain.Modules.Identity;

public sealed class StaffUser : Entity
{
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;
    public string[] Permissions { get; set; } = [];
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
}
public sealed class StaffBranch
{
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
}
public sealed class AuthSession : Entity
{
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
public sealed class RefreshCredential : Entity
{
    public Guid SessionId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset? UsedAtUtc { get; set; }
}
public sealed class AccessAudit : Entity
{
    public Guid[] BranchIds { get; set; } = [];
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string DetailsJson { get; set; } = "{}";
    public Guid ResourceId { get; set; }
}
