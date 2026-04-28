using Dapper;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.Production.QueryServices.PassStation;

internal sealed class PassStationQueryService<TDto>(
    IDbConnectionFactory connectionFactory,
    IPassStationQuerySql<TDto> sql)
    : IPassStationQueryService<TDto>
{
    public async Task<(List<TDto> Items, int TotalCount)> GetByConditionAsync(
        Pagination pagination,
        List<Guid>? deviceIds = null,
        Guid? deviceId = null,
        string? barcode = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var (conditions, parameters) = BuildWhereClause(deviceIds, deviceId, barcode, startTime, endTime);
        parameters.Add("Offset", (pagination.PageNumber - 1) * pagination.PageSize);
        parameters.Add("PageSize", pagination.PageSize);

        var dataSql = $"""
            SELECT {sql.SelectColumns}
            FROM {sql.TableName}
            {conditions}
            ORDER BY completed_time DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var countSql = $"SELECT COUNT(*) FROM {sql.TableName} {conditions}";

        var items = (await connection.QueryAsync<TDto>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        return (items, totalCount);
    }

    public async Task<TDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var detailSql = $"""
            SELECT {sql.SelectColumns}
            FROM {sql.TableName}
            WHERE id = @Id
            """;

        return await connection.QuerySingleOrDefaultAsync<TDto>(
            new CommandDefinition(detailSql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<(List<TDto> Items, int TotalCount)> GetLatest200ByDeviceAsync(
        Guid deviceId,
        Pagination pagination,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var dataSql = $"""
            WITH latest AS (
                SELECT {sql.SelectColumns},
                       ROW_NUMBER() OVER (ORDER BY completed_time DESC) AS rn
                FROM {sql.TableName}
                WHERE device_id = @DeviceId
            )
            SELECT {MapCteColumns(sql.SelectColumns)}
            FROM latest
            WHERE rn <= 200
            ORDER BY CompletedTime DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var countSql = $"""
            SELECT LEAST(COUNT(*), 200)
            FROM {sql.TableName}
            WHERE device_id = @DeviceId
            """;

        var parameters = new
        {
            DeviceId = deviceId,
            Offset = (pagination.PageNumber - 1) * pagination.PageSize,
            PageSize = pagination.PageSize
        };

        var items = (await connection.QueryAsync<TDto>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, new { DeviceId = deviceId }, cancellationToken: cancellationToken));

        return (items, totalCount);
    }

    private static (string Conditions, DynamicParameters Parameters) BuildWhereClause(
        List<Guid>? deviceIds,
        Guid? deviceId,
        string? barcode,
        DateTime? startTime,
        DateTime? endTime)
    {
        var conditions = "WHERE 1=1";
        var parameters = new DynamicParameters();

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

    private static string MapCteColumns(string selectColumns)
    {
        var aliases = selectColumns
            .Split(',')
            .Select(column =>
            {
                var parts = column.Trim().Split(" AS ", StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 1 ? parts[^1].Trim() : parts[0].Trim();
            });

        return string.Join(", ", aliases);
    }
}
