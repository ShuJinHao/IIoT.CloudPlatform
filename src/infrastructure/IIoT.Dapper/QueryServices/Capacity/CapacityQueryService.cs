using Dapper;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.QueryServices.Capacity;

public class CapacityQueryService(IDbConnectionFactory connectionFactory) : ICapacityQueryService
{
    public async Task<(List<dynamic> Items, int TotalCount)> GetDailyPagedAsync(
        Pagination pagination,
        DateOnly? date = null,
        Guid? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var conditions = "WHERE 1=1";
        var parameters = new DynamicParameters();

        if (date.HasValue)
        {
            conditions += " AND c.date = @Date";
            parameters.Add("Date", date.Value);
        }

        if (deviceId.HasValue)
        {
            conditions += " AND c.device_id = @DeviceId";
            parameters.Add("DeviceId", deviceId.Value);
        }

        var dataSql = $@"
            SELECT c.id, c.device_id, d.device_name, d.device_code,
                   c.date, c.shift_code, c.total_count, c.ok_count, c.ng_count,
                   CASE WHEN c.total_count > 0 
                        THEN ROUND(c.ok_count * 100.0 / c.total_count, 2) 
                        ELSE 0 END AS ok_rate,
                   c.reported_at
            FROM daily_capacity c
            INNER JOIN devices d ON c.device_id = d.id
            {conditions}
            ORDER BY c.date DESC, d.device_code
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $@"
            SELECT COUNT(*) 
            FROM daily_capacity c
            INNER JOIN devices d ON c.device_id = d.id
            {conditions}";

        var offset = (pagination.PageNumber - 1) * pagination.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pagination.PageSize);

        var command = new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken);
        var countCommand = new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken);

        var items = (await connection.QueryAsync(command)).ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(countCommand);

        return (items, totalCount);
    }

    public async Task<List<dynamic>> GetDeviceSummaryAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var sql = @"
            SELECT c.date, c.shift_code, c.total_count, c.ok_count, c.ng_count,
                   CASE WHEN c.total_count > 0 
                        THEN ROUND(c.ok_count * 100.0 / c.total_count, 2) 
                        ELSE 0 END AS ok_rate,
                   d.device_name, d.device_code,
                   c.reported_at
            FROM daily_capacity c
            INNER JOIN devices d ON c.device_id = d.id
            WHERE c.device_id = @DeviceId
              AND c.date >= @StartDate
              AND c.date <= @EndDate
            ORDER BY c.date DESC, c.shift_code";

        var command = new CommandDefinition(sql, new { DeviceId = deviceId, StartDate = startDate, EndDate = endDate }, cancellationToken: cancellationToken);

        return (await connection.QueryAsync(command)).ToList();
    }
}