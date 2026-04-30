using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands;

internal static class UploadMessageTypes
{
    public const string DeviceLog = "device-log";
    public const string HourlyCapacity = "hourly-capacity";

    public static string ForPassStation(string typeKey)
    {
        return $"pass-station:{typeKey}";
    }
}

internal static class UploadDeduplicationKeys
{
    public const int MaxRequestIdLength = 128;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Result<string> ForDeviceLog(ReceiveDeviceLogCommand request)
    {
        return Build(
            request.RequestId,
            new
            {
                request.DeviceId,
                Logs = (request.Logs ?? [])
                    .Select(log => new
                    {
                        log.Level,
                        log.Message,
                        LogTime = NormalizeDateTime(log.LogTime)
                    })
                    .ToArray()
            });
    }

    public static Result<string> ForHourlyCapacity(ReceiveHourlyCapacityCommand request)
    {
        return Build(
            request.RequestId,
            new
            {
                request.DeviceId,
                Date = request.Date.ToString("O", CultureInfo.InvariantCulture),
                request.ShiftCode,
                request.Hour,
                request.Minute,
                request.TimeLabel,
                request.TotalCount,
                request.OkCount,
                request.NgCount,
                request.PlcName
            });
    }

    public static Result<string> ForPassStationBatch(ReceivePassStationBatchCommand request)
    {
        return Build(
            request.RequestId,
            new
            {
                request.DeviceId,
                TypeKey = PassStationPayloadJson.NormalizeTypeKey(request.TypeKey),
                Items = (request.Items ?? [])
                    .Select(item => new
                    {
                        item.Barcode,
                        item.CellResult,
                        CompletedTime = NormalizeDateTime(item.CompletedTime),
                        Payload = PassStationPayloadJson.Canonicalize(item.Payload)
                    })
                    .ToArray()
            });
    }

    public static string ForPassStationRecord(
        string typeKey,
        Guid deviceId,
        string barcode,
        string cellResult,
        DateTime completedTime,
        string payloadJson)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                TypeKey = PassStationPayloadJson.NormalizeTypeKey(typeKey),
                DeviceId = deviceId,
                Barcode = barcode.Trim(),
                CellResult = cellResult.Trim(),
                CompletedTime = NormalizeDateTime(completedTime),
                Payload = payloadJson
            },
            SerializerOptions);

        return ComputeSha256(payload);
    }

    public static string? NormalizeRequestId(string? requestId)
    {
        requestId = requestId?.Trim();
        return string.IsNullOrWhiteSpace(requestId) ? null : requestId;
    }

    private static Result<string> Build(string? requestId, object legacyPayload)
    {
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId is not null)
        {
            if (normalizedRequestId.Length > MaxRequestIdLength)
            {
                return Result.Failure($"RequestId 不能超过 {MaxRequestIdLength} 个字符");
            }

            return Result.Success($"request:{normalizedRequestId}");
        }

        var payload = JsonSerializer.Serialize(legacyPayload, SerializerOptions);
        return Result.Success($"legacy:{ComputeSha256(payload)}");
    }

    private static string NormalizeDateTime(DateTime value)
    {
        var normalized = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return normalized.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
