using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IIoT.Services.Contracts.Events.PassStations;

public static class PassStationContentFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(PassStationBatchReceivedEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var payload = JsonSerializer.Serialize(
            new
            {
                @event.DeviceId,
                TypeKey = Normalize(@event.TypeKey),
                @event.SchemaVersion,
                ProcessType = Normalize(
                    string.IsNullOrWhiteSpace(@event.ProcessType)
                        ? @event.TypeKey
                        : @event.ProcessType),
                Items = @event.Items.Select(item => new
                {
                    Barcode = item.Barcode.Trim(),
                    CellResult = item.CellResult.Trim(),
                    CompletedTime = NormalizeDateTime(item.CompletedTime),
                    PayloadJson = CanonicalizeJson(item.PayloadJson)
                }).ToArray()
            },
            SerializerOptions);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();

    private static string NormalizeDateTime(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string CanonicalizeJson(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return JsonSerializer.Serialize(NormalizeElement(document.RootElement), SerializerOptions);
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
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
