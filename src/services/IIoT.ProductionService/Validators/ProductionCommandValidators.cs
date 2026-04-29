using FluentValidation;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.Services.Contracts.Events.DeviceLogs;

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
    private static readonly RecipeParametersJsonbValidator ParametersValidator = new();

    public CreateRecipeCommandValidator()
    {
        RuleFor(x => x.RecipeName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ProcessId).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.ParametersJsonb)
            .NotEmpty()
            .Custom((value, context) =>
            {
                foreach (var error in ParametersValidator.Validate(value))
                {
                    context.AddFailure(nameof(CreateRecipeCommand.ParametersJsonb), error);
                }
            });
    }
}

public sealed class UpgradeRecipeVersionCommandValidator : AbstractValidator<UpgradeRecipeVersionCommand>
{
    private static readonly RecipeParametersJsonbValidator ParametersValidator = new();

    public UpgradeRecipeVersionCommandValidator()
    {
        RuleFor(x => x.SourceRecipeId).NotEmpty();
        RuleFor(x => x.NewVersion).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ParametersJsonb)
            .NotEmpty()
            .Custom((value, context) =>
            {
                foreach (var error in ParametersValidator.Validate(value))
                {
                    context.AddFailure(nameof(UpgradeRecipeVersionCommand.ParametersJsonb), error);
                }
            });
    }
}

public sealed class DeleteRecipeCommandValidator : AbstractValidator<DeleteRecipeCommand>
{
    public DeleteRecipeCommandValidator()
    {
        RuleFor(x => x.RecipeId).NotEmpty();
    }
}

public sealed class ReceiveDeviceLogCommandValidator : AbstractValidator<ReceiveDeviceLogCommand>
{
    public ReceiveDeviceLogCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.RequestId)
            .MaximumLength(UploadValidationLimits.MaxRequestIdLength)
            .When(x => x.RequestId is not null);
        RuleFor(x => x.Logs)
            .NotNull()
            .NotEmpty()
            .Must(x => x is not null && x.Count <= UploadValidationLimits.MaxDeviceLogItems)
            .WithMessage($"单次设备日志上传不能超过 {UploadValidationLimits.MaxDeviceLogItems} 条。");
        RuleForEach(x => x.Logs).SetValidator(new DeviceLogItemValidator());
    }
}

public sealed class DeviceLogItemValidator : AbstractValidator<DeviceLogItem>
{
    public DeviceLogItemValidator()
    {
        RuleFor(x => x.Level)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxShortCodeLength);
        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxDeviceLogMessageLength);
        RuleFor(x => x.LogTime)
            .Must(UploadValidationRules.BeReasonableTimestamp)
            .WithMessage("日志时间必须在有效范围内。");
    }
}

public sealed class ReceiveHourlyCapacityCommandValidator : AbstractValidator<ReceiveHourlyCapacityCommand>
{
    public ReceiveHourlyCapacityCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.RequestId)
            .MaximumLength(UploadValidationLimits.MaxRequestIdLength)
            .When(x => x.RequestId is not null);
        RuleFor(x => x.Date)
            .Must(UploadValidationRules.BeReasonableDate)
            .WithMessage("产能日期必须在有效范围内。");
        RuleFor(x => x.ShiftCode)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxShortCodeLength);
        RuleFor(x => x.Hour).InclusiveBetween(0, 23);
        RuleFor(x => x.Minute).InclusiveBetween(0, 59);
        RuleFor(x => x.TimeLabel)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxShortCodeLength);
        RuleFor(x => x.TotalCount)
            .InclusiveBetween(0, UploadValidationLimits.MaxHourlyCapacityCount);
        RuleFor(x => x.OkCount)
            .InclusiveBetween(0, UploadValidationLimits.MaxHourlyCapacityCount);
        RuleFor(x => x.NgCount)
            .InclusiveBetween(0, UploadValidationLimits.MaxHourlyCapacityCount);
        RuleFor(x => x)
            .Must(x => x.OkCount + x.NgCount <= x.TotalCount)
            .WithMessage("OK 数量与 NG 数量之和不能超过总数。");
        RuleFor(x => x.PlcName)
            .MaximumLength(UploadValidationLimits.MaxMediumCodeLength)
            .When(x => x.PlcName is not null);
    }
}

public sealed class ReceiveInjectionPassCommandValidator : AbstractValidator<ReceiveInjectionPassCommand>
{
    public ReceiveInjectionPassCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.RequestId)
            .MaximumLength(UploadValidationLimits.MaxRequestIdLength)
            .When(x => x.RequestId is not null);
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(x => x is not null && x.Count <= UploadValidationLimits.MaxInjectionPassItems)
            .WithMessage($"单次注塑过站上传不能超过 {UploadValidationLimits.MaxInjectionPassItems} 条。");
        RuleForEach(x => x.Items).SetValidator(new InjectionPassItemInputValidator());
    }
}

public sealed class InjectionPassItemInputValidator : AbstractValidator<InjectionPassItemInput>
{
    public InjectionPassItemInputValidator()
    {
        RuleFor(x => x.Barcode)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxMediumCodeLength);
        RuleFor(x => x.CellResult)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxShortCodeLength);
        RuleFor(x => x.CompletedTime)
            .Must(UploadValidationRules.BeReasonableTimestamp)
            .WithMessage("完成时间必须在有效范围内。");
        RuleFor(x => x.PreInjectionTime)
            .Must(UploadValidationRules.BeReasonableTimestamp)
            .WithMessage("注塑前时间必须在有效范围内。");
        RuleFor(x => x.PostInjectionTime)
            .Must(UploadValidationRules.BeReasonableTimestamp)
            .WithMessage("注塑后时间必须在有效范围内。");
        RuleFor(x => x.PostInjectionTime)
            .GreaterThanOrEqualTo(x => x.PreInjectionTime)
            .WithMessage("注塑后时间不能早于注塑前时间。");
        RuleFor(x => x.CompletedTime)
            .GreaterThanOrEqualTo(x => x.PostInjectionTime)
            .WithMessage("完成时间不能早于注塑后时间。");
        RuleFor(x => x.PreInjectionWeight).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PostInjectionWeight).GreaterThanOrEqualTo(0);
        RuleFor(x => x.InjectionVolume).GreaterThanOrEqualTo(0);
    }
}

public sealed class ReceiveStackingPassCommandValidator : AbstractValidator<ReceiveStackingPassCommand>
{
    public ReceiveStackingPassCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.RequestId)
            .MaximumLength(UploadValidationLimits.MaxRequestIdLength)
            .When(x => x.RequestId is not null);
        RuleFor(x => x.Item).NotNull();
        RuleFor(x => x.Item)
            .SetValidator(new StackingPassItemInputValidator())
            .When(x => x.Item is not null);
    }
}

public sealed class StackingPassItemInputValidator : AbstractValidator<StackingPassItemInput>
{
    public StackingPassItemInputValidator()
    {
        RuleFor(x => x.Barcode)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxMediumCodeLength);
        RuleFor(x => x.TrayCode)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxMediumCodeLength);
        RuleFor(x => x.LayerCount).InclusiveBetween(1, 1000);
        RuleFor(x => x.SequenceNo).InclusiveBetween(1, 100000);
        RuleFor(x => x.CellResult)
            .NotEmpty()
            .MaximumLength(UploadValidationLimits.MaxShortCodeLength);
        RuleFor(x => x.CompletedTime)
            .Must(UploadValidationRules.BeReasonableTimestamp)
            .WithMessage("完成时间必须在有效范围内。");
    }
}

file static class UploadValidationRules
{
    public static bool BeReasonableTimestamp(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

        return utcValue >= new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
               && utcValue <= DateTime.UtcNow.AddDays(1);
    }

    public static bool BeReasonableDate(DateOnly value)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return value >= new DateOnly(2000, 1, 1)
               && value <= today.AddDays(1);
    }
}
