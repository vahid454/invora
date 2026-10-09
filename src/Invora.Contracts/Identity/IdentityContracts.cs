namespace Invora.Contracts.Identity;

public sealed record SetupRequest(string TradeName, string BranchCode, string BranchName, string OwnerLogin, string OwnerName, string Password);
public sealed record LoginRequest(string Login, string Password);
public sealed record StaffRequest(string Login, string DisplayName, string Password, string[] Permissions, Guid[] BranchIds);
public sealed record AccessRequest(string[] Permissions, Guid[] BranchIds, bool IsActive);
public sealed record BranchRequest(string Code, string Name);
public sealed record ProfileRequest(string TradeName, string TimeZone, int FinancialYearStartMonth);
public sealed record UserView(Guid Id, string Login, string DisplayName, bool IsOwner, bool IsActive, string[] Permissions, Guid[] BranchIds);
public sealed record SessionView(string AccessToken, DateTimeOffset ExpiresAtUtc, UserView User);
public sealed record SessionResult(SessionView Response, string RefreshToken);
public sealed record BusinessView(Guid Id, string TradeName, string Currency, string TimeZone, int FinancialYearStartMonth);
public sealed record BranchView(Guid Id, string Code, string Name, bool IsActive);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
