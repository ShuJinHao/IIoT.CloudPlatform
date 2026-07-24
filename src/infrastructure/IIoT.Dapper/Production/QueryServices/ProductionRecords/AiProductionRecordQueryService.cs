using System.Text.Json;
using Dapper;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.ProductionRecords;

internal sealed class AiProductionRecordQueryService(IDbConnectionFactory connectionFactory)
    : IAiProductionRecordQueryService
{
    private const string SelectColumns = """
        r.id AS Id,
        r.type_key AS TypeKey,
        r.device_id AS DeviceId,
        d.device_name AS DeviceName,
        r.barcode AS Barcode,
        r.cell_result AS Result,
        r.completed_time AS CompletedTime,
        r.received_at AS ReceivedAt,
        r.payload_jsonb::text AS PayloadJson
        """;

    public async Task<(List<AiProductionRecordQueryItem> Items, int TotalCount)> GetAsync(
        AiProductionRecordQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default)
    {
        if (allowedDeviceIds is { Count: 0 })
        {
            return ([], 0);
        }

        using var connection = connectionFactory.CreateConnection();
        var (conditions, parameters) = BuildWhereClause(request, allowedDeviceIds);
        parameters.Add("Offset", (request.Pagination.PageNumber - 1) * request.Pagination.PageSize);
        parameters.Add("PageSize", request.Pagination.PageSize);

        var dataSql = $"""
            SELECT {SelectColumns}
            FROM pass_station_records r
            INNER JOIN devices d ON r.device_id = d.id
            {conditions}
            ORDER BY r.completed_time DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;
        var countSql = $"""
            SELECT COUNT(*)
            FROM pass_station_records r
            INNER JOIN devices d ON r.device_id = d.id
            {conditions}
            """;

        var rows = (await connection.QueryAsync<AiProductionRecordRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        return (rows.Select(ToItem).ToList(), totalCount);
    }

    private static (string Conditions, DynamicParameters Parameters) BuildWhereClause(
        AiProductionRecordQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds)
    {
        var conditions = "WHERE r.completed_time >= @StartTime AND r.completed_time <= @EndTime";
        var parameters = new DynamicParameters();
        parameters.Add("StartTime", request.StartTime);
        parameters.Add("EndTime", request.EndTime);

        if (!string.IsNullOrWhiteSpace(request.TypeKey))
        {
            conditions += " AND r.type_key = @TypeKey";
            parameters.Add("TypeKey", request.TypeKey);
        }

        if (request.ProcessId.HasValue)
        {
            conditions += " AND d.process_id = @ProcessId";
            parameters.Add("ProcessId", request.ProcessId.Value);
        }

        if (request.DeviceId.HasValue)
        {
            conditions += " AND r.device_id = @DeviceId";
            parameters.Add("DeviceId", request.DeviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.PlcCode))
        {
            conditions += " AND r.payload_jsonb ->> 'plcCode' = @PlcCode";
            parameters.Add("PlcCode", request.PlcCode);
        }

        if (!string.IsNullOrWhiteSpace(request.PlcName))
        {
            conditions += " AND r.payload_jsonb ->> 'plcName' = @PlcName";
            parameters.Add("PlcName", request.PlcName);
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            conditions += " AND r.barcode = @Barcode";
            parameters.Add("Barcode", request.Barcode);
        }

        if (!string.IsNullOrWhiteSpace(request.Result))
        {
            conditions += " AND r.cell_result = @Result";
            parameters.Add("Result", request.Result);
        }

        if (allowedDeviceIds is { Count: > 0 })
        {
            conditions += " AND r.device_id = ANY(@AllowedDeviceIds)";
            parameters.Add("AllowedDeviceIds", allowedDeviceIds.ToArray());
        }

        return (conditions, parameters);
    }

    private static AiProductionRecordQueryItem ToItem(AiProductionRecordRow row)
    {
        return new AiProductionRecordQueryItem(
            row.Id,
            row.TypeKey,
            row.DeviceId,
            row.DeviceName,
            row.Barcode,
            row.Result,
            NormalizeUtc(row.CompletedTime),
            NormalizeUtc(row.ReceivedAt),
            ReadFields(row.PayloadJson));
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private static Dictionary<string, object?> ReadFields(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(payloadJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        return document.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => ToPlainValue(property.Value),
                StringComparer.Ordinal);
    }

    private static object? ToPlainValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private sealed class AiProductionRecordRow
    {
        public Guid Id { get; init; }

        public string TypeKey { get; init; } = string.Empty;

        public Guid DeviceId { get; init; }

        public string DeviceName { get; init; } = string.Empty;

        public string? Barcode { get; init; }

        public string? Result { get; init; }

        public DateTime? CompletedTime { get; init; }

        public DateTime? ReceivedAt { get; init; }

        public string PayloadJson { get; init; } = "{}";
    }
}
