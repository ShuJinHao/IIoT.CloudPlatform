using System.Security.Cryptography;
using System.Text;

namespace IIoT.ProductionService.Commands.DeviceLogs;

internal static class DeviceLogIdempotency
{
    public static DateTime NormalizeLogTime(DateTime logTime)
    {
        return logTime.Kind switch
        {
            DateTimeKind.Utc => logTime,
            DateTimeKind.Local => logTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(logTime, DateTimeKind.Utc)
        };
    }

    public static string CreateKey(
        Guid deviceId,
        string level,
        string message,
        DateTime normalizedLogTime)
    {
        var payload = FormattableString.Invariant(
            $"{deviceId:N}|{level}|{message}|{normalizedLogTime.Ticks}");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
