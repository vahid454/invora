using FluentValidation;
using Invora.Contracts.Identity;
namespace Invora.Application.Modules.Identity;

public sealed class SetupValidator : AbstractValidator<SetupRequest>
{
    public SetupValidator()
    {
        RuleFor(x => x.TradeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BranchCode).NotEmpty().MaximumLength(20); RuleFor(x => x.BranchName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerLogin).NotEmpty().MaximumLength(100); RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128);
    }
}
public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator() { RuleFor(x => x.Login).NotEmpty().MaximumLength(100); RuleFor(x => x.Password).NotEmpty().MaximumLength(128); }
}
public sealed class StaffValidator : AbstractValidator<StaffRequest>
{
    public StaffValidator() { RuleFor(x => x.Login).NotEmpty().MaximumLength(100); RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200); RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128); RuleFor(x => x.Permissions).NotNull(); RuleFor(x => x.BranchIds).NotEmpty(); RuleForEach(x => x.Permissions).NotEmpty().MaximumLength(100); }
}
public sealed class AccessValidator : AbstractValidator<AccessRequest>
{
    public AccessValidator() { RuleFor(x => x.Permissions).NotNull(); RuleFor(x => x.BranchIds).NotEmpty(); RuleForEach(x => x.Permissions).NotEmpty().MaximumLength(100); }
}
public sealed class BranchValidator : AbstractValidator<BranchRequest>
{
    public BranchValidator() { RuleFor(x => x.Code).NotEmpty().MaximumLength(20); RuleFor(x => x.Name).NotEmpty().MaximumLength(200); }
}
public sealed class ProfileValidator : AbstractValidator<ProfileRequest>
{
    public ProfileValidator() { RuleFor(x => x.TradeName).NotEmpty().MaximumLength(200); RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(100); RuleFor(x => x.FinancialYearStartMonth).InclusiveBetween(1, 12); }
}

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(128);
    }
}
