using System.Text.Json;
using FluentValidation;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.PassStations;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.RecordQueries;

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

public sealed class ReceivePassStationBatchCommandValidator : AbstractValidator<ReceivePassStationBatchCommand>
{
    public ReceivePassStationBatchCommandValidator(IPassStationSchemaProvider schemaProvider)
    {
        RuleFor(x => x.TypeKey).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.SchemaVersion)
            .Equal(1)
            .WithMessage("过站数据 schemaVersion 不受支持。");
        RuleFor(x => x.ProcessType)
            .MaximumLength(UploadValidationLimits.MaxShortCodeLength)
            .When(x => x.ProcessType is not null);
        RuleFor(x => x.RequestId)
            .MaximumLength(UploadValidationLimits.MaxRequestIdLength)
            .When(x => x.RequestId is not null);
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(x => x is not null && x.Count <= UploadValidationLimits.MaxPassStationItems)
            .WithMessage($"单次过站上传不能超过 {UploadValidationLimits.MaxPassStationItems} 条。");
        RuleFor(x => x)
            .Custom((command, context) =>
            {
                var definition = schemaProvider.Find(command.TypeKey ?? string.Empty);
                if (definition is null)
                {
                    context.AddFailure(nameof(ReceivePassStationBatchCommand.TypeKey), $"过站类型 [{command.TypeKey}] 不存在。");
                    return;
                }

                var processType = PassStationPayloadJson.NormalizeOptionalProcessType(command.ProcessType);
                if (processType is not null && !string.Equals(processType, definition.TypeKey, StringComparison.Ordinal))
                {
                    context.AddFailure(nameof(ReceivePassStationBatchCommand.ProcessType), "过站数据 processType 必须与 typeKey 保持一致。");
                    return;
                }

                if (command.Items is null)
                    return;

                for (var index = 0; index < command.Items.Count; index++)
                {
                    ValidateItem(command.Items[index], index, definition, context);
                }
            });
    }

    private static void ValidateItem(
        PassStationItemInput item,
        int index,
        PassStationTypeDefinitionDto definition,
        ValidationContext<ReceivePassStationBatchCommand> context)
    {
        var prefix = $"{nameof(ReceivePassStationBatchCommand.Items)}[{index}]";
        if (string.IsNullOrWhiteSpace(item.Barcode))
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.Barcode)}", "过站条码不能为空。");
        if (item.Barcode?.Length > UploadValidationLimits.MaxMediumCodeLength)
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.Barcode)}", $"过站条码不能超过 {UploadValidationLimits.MaxMediumCodeLength} 个字符。");
        if (string.IsNullOrWhiteSpace(item.CellResult))
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.CellResult)}", "过站结果不能为空。");
        if (item.CellResult?.Length > UploadValidationLimits.MaxShortCodeLength)
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.CellResult)}", $"过站结果不能超过 {UploadValidationLimits.MaxShortCodeLength} 个字符。");
        if (!UploadValidationRules.BeReasonableTimestamp(item.CompletedTime))
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.CompletedTime)}", "完成时间必须在有效范围内。");
        if (item.Payload.ValueKind != JsonValueKind.Object)
        {
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.Payload)}", "过站扩展数据必须是 JSON 对象。");
            return;
        }

        var payload = item.Payload.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        if (payload.Count > UploadValidationLimits.MaxPassStationPayloadFields)
            context.AddFailure($"{prefix}.{nameof(PassStationItemInput.Payload)}", $"过站扩展字段不能超过 {UploadValidationLimits.MaxPassStationPayloadFields} 个。");

        var knownFields = definition.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        foreach (var actualField in payload.Keys)
        {
            if (!knownFields.ContainsKey(actualField))
                context.AddFailure($"{prefix}.Payload.{actualField}", $"字段 [{actualField}] 不属于过站类型 [{definition.TypeKey}]。");
        }

        foreach (var field in definition.Fields)
        {
            payload.TryGetValue(field.Key, out var value);
            ValidatePayloadField(prefix, field, value, context);
        }
    }

    private static void ValidatePayloadField(
        string prefix,
        PassStationFieldDefinitionDto field,
        JsonElement value,
        ValidationContext<ReceivePassStationBatchCommand> context)
    {
        var fieldName = $"{prefix}.Payload.{field.Key}";
        var missing = value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
        if (field.Required && missing)
        {
            context.AddFailure(fieldName, $"字段 [{field.Label}] 不能为空。");
            return;
        }

        if (missing)
            return;

        switch (field.Type)
        {
            case PassStationFieldTypes.String:
            case PassStationFieldTypes.Enum:
            case PassStationFieldTypes.DateTime:
                ValidateStringLikeField(fieldName, field, value, context);
                break;
            case PassStationFieldTypes.Number:
            case PassStationFieldTypes.Integer:
                ValidateNumberField(fieldName, field, value, context);
                break;
            case PassStationFieldTypes.Boolean:
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    context.AddFailure(fieldName, $"字段 [{field.Label}] 必须是布尔值。");
                break;
        }
    }

    private static void ValidateStringLikeField(
        string fieldName,
        PassStationFieldDefinitionDto field,
        JsonElement value,
        ValidationContext<ReceivePassStationBatchCommand> context)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            context.AddFailure(fieldName, $"字段 [{field.Label}] 必须是字符串。");
            return;
        }

        var text = value.GetString() ?? string.Empty;
        if (field.Required && string.IsNullOrWhiteSpace(text))
            context.AddFailure(fieldName, $"字段 [{field.Label}] 不能为空。");
        if (field.MaxLength is not null && text.Length > field.MaxLength)
            context.AddFailure(fieldName, $"字段 [{field.Label}] 不能超过 {field.MaxLength} 个字符。");
        if (field.Type == PassStationFieldTypes.Enum && field.Options is not null && !field.Options.Contains(text))
            context.AddFailure(fieldName, $"字段 [{field.Label}] 不在允许选项内。");
        if (field.Type == PassStationFieldTypes.DateTime && !DateTime.TryParse(text, out _))
            context.AddFailure(fieldName, $"字段 [{field.Label}] 必须是有效时间。");
    }

    private static void ValidateNumberField(
        string fieldName,
        PassStationFieldDefinitionDto field,
        JsonElement value,
        ValidationContext<ReceivePassStationBatchCommand> context)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            context.AddFailure(fieldName, $"字段 [{field.Label}] 必须是数字。");
            return;
        }

        if (field.Type == PassStationFieldTypes.Integer && decimal.Truncate(number) != number)
            context.AddFailure(fieldName, $"字段 [{field.Label}] 必须是整数。");
        if (field.Min is not null && number < field.Min)
            context.AddFailure(fieldName, $"字段 [{field.Label}] 不能小于 {field.Min}。");
        if (field.Max is not null && number > field.Max)
            context.AddFailure(fieldName, $"字段 [{field.Label}] 不能大于 {field.Max}。");
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
