using FluentValidation;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.Recipes;

namespace IIoT.ProductionService.Validators;

public sealed class RefreshEdgeDeviceIdentityCommandValidator : AbstractValidator<RefreshEdgeDeviceIdentityCommand>
{
    public RefreshEdgeDeviceIdentityCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class RegisterDeviceCommandValidator : AbstractValidator<RegisterDeviceCommand>
{
    public RegisterDeviceCommandValidator()
    {
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ProcessId).NotEmpty();
    }
}

public sealed class UpdateDeviceProfileCommandValidator : AbstractValidator<UpdateDeviceProfileCommand>
{
    public UpdateDeviceProfileCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(128);
    }
}

public sealed class DeleteDeviceCommandValidator : AbstractValidator<DeleteDeviceCommand>
{
    public DeleteDeviceCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class CreateRecipeCommandValidator : AbstractValidator<CreateRecipeCommand>
{
    public CreateRecipeCommandValidator()
    {
        RuleFor(x => x.RecipeName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ProcessId).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.ParametersJsonb).NotEmpty();
    }
}

public sealed class UpgradeRecipeVersionCommandValidator : AbstractValidator<UpgradeRecipeVersionCommand>
{
    public UpgradeRecipeVersionCommandValidator()
    {
        RuleFor(x => x.SourceRecipeId).NotEmpty();
        RuleFor(x => x.NewVersion).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ParametersJsonb).NotEmpty();
    }
}

public sealed class DeleteRecipeCommandValidator : AbstractValidator<DeleteRecipeCommand>
{
    public DeleteRecipeCommandValidator()
    {
        RuleFor(x => x.RecipeId).NotEmpty();
    }
}
