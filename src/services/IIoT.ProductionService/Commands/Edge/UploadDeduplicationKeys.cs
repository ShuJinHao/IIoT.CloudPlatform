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
    public const string PassStationInjection = "pass-station-injection";
    public const string PassStationStacking = "pass-station-stacking";
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

    public static Result<string> ForInjectionPass(ReceiveInjectionPassCommand request)
    {
        return Build(
            request.RequestId,
            new
            {
                request.DeviceId,
                Items = (request.Items ?? [])
                    .Select(item => new
                    {
                        item.Barcode,
                        item.CellResult,
                        CompletedTime = NormalizeDateTime(item.CompletedTime),
                        PreInjectionTime = NormalizeDateTime(item.PreInjectionTime),
                        item.PreInjectionWeight,
                        PostInjectionTime = NormalizeDateTime(item.PostInjectionTime),
                        item.PostInjectionWeight,
                        item.InjectionVolume
                    })
                    .ToArray()
            });
    }

    public static Result<string> ForStackingPass(ReceiveStackingPassCommand request)
    {
        return Build(
            request.RequestId,
            new
            {
                request.DeviceId,
                Item = request.Item is null
                    ? null
                    : new
                    {
                        request.Item.Barcode,
                        request.Item.TrayCode,
                        request.Item.LayerCount,
                        request.Item.SequenceNo,
                        request.Item.CellResult,
                        CompletedTime = NormalizeDateTime(request.Item.CompletedTime)
                    }
            });
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
