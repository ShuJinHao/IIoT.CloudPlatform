using Dapper;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.Production.QueryServices.PassStation;

public class PassStationQueryService(IDbConnectionFactory connectionFactory) : IPassStationQueryService
{
    public async Task<(List<dynamic> Items, int TotalCount)> GetInjectionByConditionAsync(
        Pagination pagination,
        List<Guid>? deviceIds = null,
        Guid? deviceId = null,
        string? barcode = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var conditions = "WHERE 1=1";
        var parameters = new DynamicParameters();

        if (deviceIds != null && deviceIds.Count > 0)
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

        var dataSql = $@"
            SELECT id, device_id, barcode, cell_result,
                   pre_injection_time, pre_injection_weight,
                   post_injection_time, post_injection_weight,
                   injection_volume, completed_time, received_at
            FROM pass_data_injection
            {conditions}
            ORDER BY completed_time DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $"SELECT COUNT(*) FROM pass_data_injection {conditions}";

        var offset = (pagination.PageNumber - 1) * pagination.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pagination.PageSize);

        var command = new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken);
        var countCommand = new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken);

        var items = (await connection.QueryAsync(command)).ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(countCommand);

        return (items, totalCount);
    }

    public async Task<dynamic?> GetInjectionDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var sql = "SELECT * FROM pass_data_injection WHERE id = @Id";
        var command = new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync(command);
    }

    public async Task<(List<dynamic> Items, int TotalCount)> GetInjectionLatest200ByDeviceAsync(
        Guid deviceId,
        Pagination pagination,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        // CTE 先锁定该机台最新 200 条，外层再分页，前端最多翻到第 200 条
        var dataSql = @"
            WITH latest AS (
                SELECT id, device_id, barcode, cell_result,
                       pre_injection_time, pre_injection_weight,
                       post_injection_time, post_injection_weight,
                       injection_volume, completed_time, received_at,
                       ROW_NUMBER() OVER (ORDER BY completed_time DESC) AS rn
                FROM pass_data_injection
                WHERE device_id = @DeviceId
            )
            SELECT id, device_id, barcode, cell_result,
                   pre_injection_time, pre_injection_weight,
                   post_injection_time, post_injection_weight,
                   injection_volume, completed_time, received_at
            FROM latest
            WHERE rn <= 200
            ORDER BY completed_time DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = @"
            SELECT LEAST(COUNT(*), 200)
            FROM pass_data_injection
            WHERE device_id = @DeviceId";

        var offset = (pagination.PageNumber - 1) * pagination.PageSize;
        var parameters = new { DeviceId = deviceId, Offset = offset, PageSize = pagination.PageSize };

        var command      = new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken);
        var countCommand = new CommandDefinition(countSql, new { DeviceId = deviceId }, cancellationToken: cancellationToken);

        var items      = (await connection.QueryAsync(command)).ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(countCommand);

        return (items, totalCount);
    }
}