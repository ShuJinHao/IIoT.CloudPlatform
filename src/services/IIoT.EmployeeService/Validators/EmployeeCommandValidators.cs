using FluentValidation;
using IIoT.EmployeeService.Commands.Employees;

namespace IIoT.EmployeeService.Validators;

public sealed class OnboardEmployeeCommandValidator : AbstractValidator<OnboardEmployeeCommand>
{
    public OnboardEmployeeCommandValidator()
    {
        RuleFor(x => x.EmployeeNo).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RealName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class UpdateEmployeeProfileCommandValidator : AbstractValidator<UpdateEmployeeProfileCommand>
{
    public UpdateEmployeeProfileCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.RealName).NotEmpty().MaximumLength(64);
    }
}

public sealed class UpdateEmployeeAccessCommandValidator : AbstractValidator<UpdateEmployeeAccessCommand>
{
    public UpdateEmployeeAccessCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.DeviceIds).NotNull();
        RuleForEach(x => x.DeviceIds).NotEmpty();
    }
}

public sealed class DeactivateEmployeeCommandValidator : AbstractValidator<DeactivateEmployeeCommand>
{
    public DeactivateEmployeeCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}

public sealed class TerminateEmployeeCommandValidator : AbstractValidator<TerminateEmployeeCommand>
{
    public TerminateEmployeeCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}
