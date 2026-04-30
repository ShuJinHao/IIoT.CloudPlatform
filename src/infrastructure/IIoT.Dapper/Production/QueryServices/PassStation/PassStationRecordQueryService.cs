using System.Text.Json;
using Dapper;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.PassStation;

public sealed class PassStationRecordQueryService(IDbConnectionFactory connectionFactory)
    : IPassStationRecordQueryService
{
    private const string SelectColumns = """
        id AS Id,
        device_id AS DeviceId,
        barcode AS Barcode,
        cell_result AS CellResult,
        completed_time AS CompletedTime,
        received_at AS ReceivedAt,
        payload_jsonb::text AS PayloadJson
        """;

    public async Task<(List<PassStationListItemDto> Items, int TotalCount)> GetByConditionAsync(
        PassStationQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var (conditions, parameters) = BuildWhereClause(
            request.TypeKey,
            allowedDeviceIds,
            request.DeviceId,
            request.Barcode,
            request.StartTime,
            request.EndTime);
        parameters.Add("Offset", (request.Pagination.PageNumber - 1) * request.Pagination.PageSize);
        parameters.Add("PageSize", request.Pagination.PageSize);

        var dataSql = $"""
            SELECT {SelectColumns}
            FROM pass_station_records
            {conditions}
            ORDER BY completed_time DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var countSql = $"SELECT COUNT(*) FROM pass_station_records {conditions}";

        var rows = (await connection.QueryAsync<PassStationRecordRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        return (rows.Select(ToListItem).ToList(), totalCount);
    }

    public async Task<PassStationDetailDto?> GetDetailAsync(
        string typeKey,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var detailSql = $"""
            SELECT {SelectColumns}
            FROM pass_station_records
            WHERE type_key = @TypeKey AND id = @Id
            """;

        var row = await connection.QuerySingleOrDefaultAsync<PassStationRecordRow>(
            new CommandDefinition(
                detailSql,
                new { TypeKey = typeKey, Id = id },
                cancellationToken: cancellationToken));

        return row is null ? null : ToDetail(row);
    }

    private static (string Conditions, DynamicParameters Parameters) BuildWhereClause(
        string typeKey,
        IReadOnlyCollection<Guid>? deviceIds,
        Guid? deviceId,
        string? barcode,
        DateTime? startTime,
        DateTime? endTime)
    {
        var conditions = "WHERE type_key = @TypeKey";
        var parameters = new DynamicParameters();
        parameters.Add("TypeKey", typeKey);

        if (deviceIds is { Count: > 0 })
        {
            conditions += " AND device_id = ANY(@DeviceIds)";
            parameters.Add("DeviceIds", deviceIds.ToArray());
        }

        if (deviceId.HasValue)
        {
            conditions += " AND device_id = @DeviceId";
            parameters.Add("DeviceId", deviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            conditions += " AND barcode = @Barcode";
            parameters.Add("Barcode", barcode);
        }

        if (startTime.HasValue)
        {
            conditions += " AND completed_time >= @StartTime";
            parameters.Add("StartTime", startTime.Value);
        }

        if (endTime.HasValue)
        {
            conditions += " AND completed_time <= @EndTime";
            parameters.Add("EndTime", endTime.Value);
        }

        return (conditions, parameters);
    }

    private static PassStationListItemDto ToListItem(PassStationRecordRow row)
    {
        return new PassStationListItemDto(
            row.Id,
            row.DeviceId,
            row.Barcode,
            row.CellResult,
            NormalizeUtc(row.CompletedTime),
            NormalizeUtc(row.ReceivedAt),
            ReadFields(row.PayloadJson));
    }

    private static PassStationDetailDto ToDetail(PassStationRecordRow row)
    {
        return new PassStationDetailDto(
            row.Id,
            row.DeviceId,
            row.Barcode,
            row.CellResult,
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

    private sealed class PassStationRecordRow
    {
        public Guid Id { get; init; }

        public Guid DeviceId { get; init; }

        public string? Barcode { get; init; }

        public string? CellResult { get; init; }

        public DateTime? CompletedTime { get; init; }

        public DateTime? ReceivedAt { get; init; }

        public string PayloadJson { get; init; } = "{}";
    }
}
