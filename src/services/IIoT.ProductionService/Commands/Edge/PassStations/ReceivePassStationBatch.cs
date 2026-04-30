using System.Text.Json;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public sealed record PassStationBatchUploadRequest(
    Guid DeviceId,
    List<PassStationItemInput> Items,
    string? RequestId = null);

public sealed record ReceivePassStationBatchCommand(
    string TypeKey,
    Guid DeviceId,
    List<PassStationItemInput> Items,
    string? RequestId = null) : IDeviceCommand<Result<bool>>;

public sealed record PassStationItemInput(
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    JsonElement Payload);

public sealed class ReceivePassStationBatchHandler(
    IPassStationReceiveService receiveService,
    IPassStationSchemaProvider schemaProvider)
    : ICommandHandler<ReceivePassStationBatchCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ReceivePassStationBatchCommand request,
        CancellationToken cancellationToken)
    {
        var typeKey = PassStationPayloadJson.NormalizeTypeKey(request.TypeKey);
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
