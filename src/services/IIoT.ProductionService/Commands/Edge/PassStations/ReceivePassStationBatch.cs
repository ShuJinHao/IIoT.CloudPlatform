using System.Text.Json;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Uploads;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public sealed record PassStationBatchUploadRequest(
    Guid DeviceId,
    List<PassStationItemInput> Items,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null);

public sealed record ProcessRecordUploadRequest(
    string? TypeKey,
    string? ProcessType,
    int SchemaVersion,
    Guid DeviceId,
    List<ProcessRecordItemInput>? Records);

public sealed record ProcessRecordItemInput(
    string? TypeKey,
    string? ProcessType,
    int SchemaVersion,
    Guid DeviceId,
    string? Barcode,
    bool? CellResult,
    DateTime? CompletedTime,
    JsonElement Payload);

public sealed record ReceivePassStationBatchCommand(
    string TypeKey,
    Guid DeviceId,
    List<PassStationItemInput> Items,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null) : IDeviceCommand<Result<EdgeUploadAcceptedResponse>>;

public sealed record PassStationItemInput(
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    JsonElement Payload);

public static class ProcessRecordUploadRequestMapper
{
    public static Result<ReceivePassStationBatchCommand> ToPassStationCommand(
        ProcessRecordUploadRequest? request)
    {
        if (request is null)
            return Result.Invalid("process-records 请求体不能为空。");

        if (string.IsNullOrWhiteSpace(request.TypeKey))
            return Result.Invalid("process-records typeKey 不能为空。");

        if (string.IsNullOrWhiteSpace(request.ProcessType))
            return Result.Invalid("process-records processType 不能为空。");

        if (request.SchemaVersion != 1)
            return Result.Invalid($"过站数据 schemaVersion [{request.SchemaVersion}] 不受支持。");

        if (request.DeviceId == Guid.Empty)
            return Result.Invalid("process-records deviceId 不能为空。");

        if (request.Records is null || request.Records.Count == 0)
            return Result.Invalid("process-records records 不能为空。");

        var typeKey = PassStationPayloadJson.NormalizeTypeKey(request.TypeKey);
        var processType = PassStationPayloadJson.NormalizeOptionalProcessType(request.ProcessType)!;
        if (!string.Equals(typeKey, processType, StringComparison.Ordinal))
            return Result.Invalid("process-records 顶层 processType 必须与 typeKey 保持一致。");

        var items = new List<PassStationItemInput>(request.Records.Count);
        for (var index = 0; index < request.Records.Count; index++)
        {
            var record = request.Records[index];
            var prefix = $"records[{index}]";

            if (string.IsNullOrWhiteSpace(record.TypeKey))
                return Result.Invalid($"{prefix}.typeKey 不能为空。");

            if (string.IsNullOrWhiteSpace(record.ProcessType))
                return Result.Invalid($"{prefix}.processType 不能为空。");

            var recordTypeKey = PassStationPayloadJson.NormalizeTypeKey(record.TypeKey);
            var recordProcessType = PassStationPayloadJson.NormalizeOptionalProcessType(record.ProcessType);
            if (!string.Equals(recordTypeKey, typeKey, StringComparison.Ordinal)
                || !string.Equals(recordProcessType, processType, StringComparison.Ordinal))
            {
                return Result.Invalid($"{prefix}.typeKey/processType 必须与顶层保持一致。");
            }

            if (record.SchemaVersion != request.SchemaVersion)
                return Result.Invalid($"{prefix}.schemaVersion 必须与顶层 schemaVersion 保持一致。");

            if (record.DeviceId != request.DeviceId)
                return Result.Invalid($"{prefix}.deviceId 必须与顶层 deviceId 保持一致。");

            if (record.CellResult is null)
                return Result.Invalid($"{prefix}.cellResult 不能为空。");

            if (record.CompletedTime is null)
                return Result.Invalid($"{prefix}.completedTime 不能为空。");

            items.Add(new PassStationItemInput(
                record.Barcode ?? string.Empty,
                record.CellResult.Value ? "OK" : "NG",
                record.CompletedTime.Value,
                record.Payload));
        }

        return Result.Success(new ReceivePassStationBatchCommand(
            typeKey,
            request.DeviceId,
            items,
            SchemaVersion: request.SchemaVersion,
            ProcessType: processType));
    }
}

public sealed class ReceivePassStationBatchHandler(
    IPassStationReceiveService receiveService,
    IPassStationSchemaProvider schemaProvider)
    : ICommandHandler<ReceivePassStationBatchCommand, Result<EdgeUploadAcceptedResponse>>
{
    public async Task<Result<EdgeUploadAcceptedResponse>> Handle(
        ReceivePassStationBatchCommand request,
        CancellationToken cancellationToken)
    {
        var typeKey = PassStationPayloadJson.NormalizeTypeKey(request.TypeKey);
        if (request.SchemaVersion != 1)
            return Result.Invalid($"过站数据 schemaVersion [{request.SchemaVersion}] 不受支持。");

        var processType = PassStationPayloadJson.NormalizeOptionalProcessType(request.ProcessType) ?? typeKey;
        if (!string.Equals(processType, typeKey, StringComparison.Ordinal))
            return Result.Invalid("过站数据 processType 必须与 typeKey 保持一致。");

        var definition = schemaProvider.Find(typeKey);
        if (definition is null)
            return Result.NotFound($"过站类型 [{typeKey}] 不存在。");

        var deduplicationKey = UploadDeduplicationKeys.ForPassStationBatch(request);
        if (!deduplicationKey.IsSuccess)
            return Result.Failure(deduplicationKey.Errors?.ToArray() ?? []);

        var eventItems = request.Items
            .Select(item =>
            {
                var payloadJson = PassStationPayloadJson.Canonicalize(item.Payload);
                return new PassStationBatchItem
                {
                    Barcode = item.Barcode.Trim(),
                    CellResult = item.CellResult.Trim(),
                    CompletedTime = NormalizeDateTime(item.CompletedTime),
                    PayloadJson = payloadJson,
                    DeduplicationKey = UploadDeduplicationKeys.ForPassStationRecord(
                        typeKey,
                        request.DeviceId,
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
            TypeKey = definition.TypeKey,
            ProcessType = processType,
            SchemaVersion = request.SchemaVersion,
            Items = eventItems
        };

        return await receiveService.ValidateAndRegisterAsync(
            request.DeviceId,
            eventItems.Length,
            UploadMessageTypes.ForPassStation(definition.TypeKey),
            UploadDeduplicationKeys.NormalizeRequestId(request.RequestId),
            deduplicationKey.Value!,
            @event,
            cancellationToken);
    }

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

    public static string NormalizeTypeKey(string typeKey)
    {
        return typeKey.Trim().ToLowerInvariant();
    }

    public static string? NormalizeOptionalProcessType(string? processType)
    {
        processType = processType?.Trim();
        return string.IsNullOrWhiteSpace(processType) ? null : processType.ToLowerInvariant();
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
