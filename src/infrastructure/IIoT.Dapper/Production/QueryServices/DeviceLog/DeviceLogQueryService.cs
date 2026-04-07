using Dapper;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.Production.QueryServices.DeviceLog;

public class DeviceLogQueryService(IDbConnectionFactory connectionFactory) : IDeviceLogQueryService
{
    public async Task<(List<dynamic> Items, int TotalCount)> GetLogsByConditionAsync(
        Pagination pagination,
        Guid deviceId,
        string? level = null,
        string? keyword = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        // device_id 必传，先走索引缩小范围
        var conditions = "WHERE l.device_id = @DeviceId";
        var parameters = new DynamicParameters();
        parameters.Add("DeviceId", deviceId);

        if (!string.IsNullOrWhiteSpace(level))
        {
            conditions += " AND l.level = @Level";
            parameters.Add("Level", level);
        }

        if (startTime.HasValue)
        {
            conditions += " AND l.log_time >= @StartTime";
            parameters.Add("StartTime", startTime.Value);
        }

        if (endTime.HasValue)
        {
            conditions += " AND l.log_time <= @EndTime";
            parameters.Add("EndTime", endTime.Value);
        }

        // 模糊搜索放最后，在索引已经缩小的数据集上做过滤
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions += " AND l.message LIKE @Keyword";
            parameters.Add("Keyword", $"%{keyword}%");
        }

        var dataSql = $@"
            SELECT l.id, l.device_id, d.device_name,
                   l.level, l.message, l.log_time, l.received_at
            FROM device_logs l
            INNER JOIN devices d ON l.device_id = d.id
            {conditions}
            ORDER BY l.log_time DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $@"
            SELECT COUNT(*)
            FROM device_logs l
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
}