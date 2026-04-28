using FluentValidation;

namespace IIoT.IdentityService.Commands;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(x => x.EmployeeNo).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class EdgeOperatorLoginCommandValidator : AbstractValidator<EdgeOperatorLoginCommand>
{
    public EdgeOperatorLoginCommandValidator()
    {
        RuleFor(x => x.EmployeeNo).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class RefreshHumanIdentityCommandValidator : AbstractValidator<RefreshHumanIdentityCommand>
{
    public RefreshHumanIdentityCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class DefineRolePolicyCommandValidator : AbstractValidator<DefineRolePolicyCommand>
{
    public DefineRolePolicyCommandValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class UpdateRolePermissionsCommandValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class UpdateUserPermissionsCommandValidator : AbstractValidator<UpdateUserPermissionsCommand>
{
    public UpdateUserPermissionsCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Permissions).NotNull();
    }
}
