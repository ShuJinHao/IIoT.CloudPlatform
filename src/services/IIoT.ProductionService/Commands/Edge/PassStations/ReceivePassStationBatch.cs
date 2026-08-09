using System.Text.Json;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Uploads;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public sealed record PassStationBatchUploadRequest(
    Guid DeviceId,
    List<PassStationItemInput> Items,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null,
    string? ClientCode = null);

public sealed record ReceivePassStationBatchCommand(
    string TypeKey,
    Guid DeviceId,
    List<PassStationItemInput> Items,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null,
    string? ClientCode = null) : IDeviceCommand<Result<EdgeUploadAcceptedResponse>>;

public sealed record PassStationItemInput(
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    JsonElement Payload,
    string? CompletionId = null,
    string? ClientCode = null,
    string? TypeKey = null,
    string? PlcCode = null);

public sealed class ReceivePassStationBatchHandler(
    IPassStationReceiveService receiveService,
    IDevicePluginDataCapabilityResolver capabilityResolver,
    ICurrentUser currentUser)
    : ICommandHandler<ReceivePassStationBatchCommand, Result<EdgeUploadAcceptedResponse>>
{
    public async Task<Result<EdgeUploadAcceptedResponse>> Handle(
        ReceivePassStationBatchCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.DeviceId != request.DeviceId
            || string.IsNullOrWhiteSpace(currentUser.ClientCode))
        {
            return Result.Forbidden("JWT 设备身份与上报目标设备不一致。");
        }
        if (request.SchemaVersion is not 1 and not 2 and not 3)
            return Result.Invalid($"过站数据 schemaVersion [{request.SchemaVersion}] 不受支持。");

        var normalizedProcessType = PassStationPayloadJson.NormalizeOptionalProcessType(request.ProcessType);
        if (request.SchemaVersion >= 2 && normalizedProcessType is null)
            return Result.Invalid("过站 v2/v3 数据必须提供 processType。");
        var requestedClientCode = request.ClientCode?.Trim().ToUpperInvariant();
        if (request.SchemaVersion == 3 && string.IsNullOrWhiteSpace(requestedClientCode))
            return Result.Invalid("过站 v3 数据必须提供 clientCode。");
        if (request.SchemaVersion == 3
            && (request.Items.Any(item => string.IsNullOrWhiteSpace(item.CompletionId))
                || request.Items
                    .Select(item => item.CompletionId!.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .Count() != request.Items.Count))
        {
            return Result.Invalid("过站 v3 每条事实必须提供且不重复的 completionId。");
        }
        if (request.SchemaVersion == 3
            && request.Items.Any(item =>
                !string.Equals(
                    item.ClientCode?.Trim().ToUpperInvariant(),
                    requestedClientCode,
                    StringComparison.Ordinal)))
        {
            return Result.Invalid("过站 v3 每条事实的 clientCode 必须与批次身份一致。");
        }

        var requestedTypeKey = PassStationPayloadJson.NormalizeTypeKey(request.TypeKey);
        if (request.SchemaVersion == 3
            && request.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.TypeKey)
                || !string.Equals(
                    PassStationPayloadJson.NormalizeTypeKey(item.TypeKey),
                    requestedTypeKey,
                    StringComparison.Ordinal)))
        {
            return Result.Invalid("过站 v3 每条事实的 typeKey 必须与请求路由一致。");
        }

        var resolved = await capabilityResolver.ResolveAsync(
            request.DeviceId,
            request.TypeKey,
            cancellationToken);
        if (!resolved.IsSuccess)
            return Result.From(resolved);

        var registeredProcessType = PassStationPayloadJson.NormalizeOptionalProcessType(
            resolved.Value!.ProcessType);
        if (registeredProcessType is null)
            return Result.Invalid("设备未绑定有效工序。");
        if (!string.Equals(
                currentUser.ClientCode.Trim().ToUpperInvariant(),
                resolved.Value.ClientCode,
                StringComparison.Ordinal)
            || (requestedClientCode is not null
                && !string.Equals(
                    requestedClientCode,
                    resolved.Value.ClientCode,
                    StringComparison.Ordinal)))
        {
            return Result.Forbidden("上报 ClientCode 与 JWT 及 Cloud 设备身份不一致。");
        }
        if (normalizedProcessType is not null
            && !string.Equals(
                normalizedProcessType,
                registeredProcessType,
                StringComparison.Ordinal))
        {
            return Result.Forbidden("上报工序与设备在 Cloud 登记工序不一致。");
        }

        var typeKey = PassStationPayloadJson.NormalizeTypeKey(
            resolved.Value.Capability.TypeKey);
        var processType = registeredProcessType;
        var payloadErrors = ValidateCapabilityPayloads(
            request.Items,
            resolved.Value.Capability);
        if (payloadErrors.Count > 0)
            return Result.Invalid(payloadErrors.ToArray());

        var canonicalRequest = request with
        {
            TypeKey = typeKey,
            ProcessType = processType,
            ClientCode = requestedClientCode
        };
        var deduplicationKey = UploadDeduplicationKeys.ForPassStationBatch(
            canonicalRequest);
        if (!deduplicationKey.IsSuccess)
            return Result.Failure(deduplicationKey.Errors?.ToArray() ?? []);

        var eventItems = request.Items
            .Select(item =>
            {
                var payloadJson = PassStationPayloadJson.Canonicalize(item.Payload);
                return new PassStationBatchItem
                {
                    CompletionId = item.CompletionId?.Trim(),
                    Barcode = item.Barcode.Trim(),
                    CellResult = item.CellResult.Trim(),
                    CompletedTime = NormalizeDateTime(item.CompletedTime),
                    PayloadJson = payloadJson,
                    DeduplicationKey = UploadDeduplicationKeys.ForPassStationRecord(
                        typeKey,
                        request.DeviceId,
                        item.CompletionId,
                        item.Barcode,
                        item.CellResult,
                        item.CompletedTime,
                        payloadJson)
                };
            })
            .ToArray();

        var @event = new PassStationBatchReceivedEvent
        {
            DeviceId = request.DeviceId,
            TypeKey = typeKey,
            ProcessType = processType,
            SchemaVersion = request.SchemaVersion,
            Items = eventItems
        };
        var contentFingerprint = PassStationContentFingerprint.Compute(@event);

        return await receiveService.ValidateAndRegisterAsync(
            request.DeviceId,
            eventItems.Length,
            UploadMessageTypes.ForPassStation(typeKey),
            UploadDeduplicationKeys.NormalizeRequestId(request.RequestId),
            deduplicationKey.Value!,
            contentFingerprint,
            @event,
            cancellationToken);
    }

    private static List<string> ValidateCapabilityPayloads(
        IReadOnlyList<PassStationItemInput> items,
        PluginDataCapability capability)
    {
        var errors = new List<string>();
        var fields = capability.Fields.ToDictionary(
            field => field.Name,
            StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Payload.ValueKind != JsonValueKind.Object)
                continue;

            var payload = item.Payload.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value,
                StringComparer.Ordinal);
            foreach (var actual in payload)
            {
                if (!fields.ContainsKey(actual.Key)
                    && !PassStationPayloadJson.IsAcceptedTransportMetadata(actual.Key))
                {
                    errors.Add(
                        $"Items[{index}].Payload.{actual.Key}: 字段不属于当前安装插件版本的 [{capability.TypeKey}] Schema。");
                }
            }

            foreach (var field in capability.Fields)
            {
                var suppliedByEnvelope = field.Name is
                    "clientCode" or "completedTime" or "cellResult"
                    or "completionId";
                payload.TryGetValue(field.Name, out var value);
                var missing = value.ValueKind is
                    JsonValueKind.Undefined or JsonValueKind.Null;
                if (!field.Nullable && missing && !suppliedByEnvelope)
                {
                    errors.Add(
                        $"Items[{index}].Payload.{field.Name}: 必填字段缺失。");
                    continue;
                }
                if (missing || suppliedByEnvelope)
                    continue;
                if (!MatchesDataType(value, field.DataType))
                {
                    errors.Add(
                        $"Items[{index}].Payload.{field.Name}: 字段类型必须为 {field.DataType}。");
                }
            }
        }

        return errors;
    }

    private static bool MatchesDataType(JsonElement value, string dataType)
        => dataType switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "datetime" => value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out _),
            "integer" => value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out _),
            "decimal" => value.ValueKind == JsonValueKind.Number
                && value.TryGetDecimal(out _),
            "boolean" => value.ValueKind is
                JsonValueKind.True or JsonValueKind.False,
            _ => false
        };

    private static DateTime NormalizeDateTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

internal static class PassStationPayloadJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AcceptedTransportMetadataFields = new(
        [
            "processType",
            "displayLabel",
            "deviceName",
            "deviceCode",
            "plcDeviceId",
            "cellResult",
            "completedTime",
            "uploadTargets",
            "clipSlot",
            "clipNo"
        ],
        StringComparer.Ordinal);

    public static string NormalizeTypeKey(string typeKey)
    {
        return typeKey.Trim().ToLowerInvariant();
    }

    public static string? NormalizeOptionalProcessType(string? processType)
    {
        if (string.IsNullOrWhiteSpace(processType))
            return null;
        try
        {
            return BusinessIdentityNormalization.NormalizeClassificationCode(
                processType,
                nameof(processType));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static bool IsAcceptedTransportMetadata(string fieldName)
    {
        return AcceptedTransportMetadataFields.Contains(fieldName);
    }

    public static string Canonicalize(JsonElement payload)
    {
        var normalized = NormalizeElement(payload);
        return JsonSerializer.Serialize(normalized, SerializerOptions);
    }

    public static IReadOnlyDictionary<string, object?> ToDictionary(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        return payload.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(
                property => property.Name,
                property => NormalizeElement(property.Value),
                StringComparer.Ordinal);
    }

    private static object? NormalizeElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToDictionary(
                    property => property.Name,
                    property => NormalizeElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(NormalizeElement)
                .ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
