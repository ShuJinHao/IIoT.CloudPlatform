using Dapper;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.Production.QueryServices.DeviceLog;

public class DeviceLogQueryService(IDbConnectionFactory connectionFactory) : IDeviceLogQueryService
{
    public async Task<(List<DeviceLogListItemDto> Items, int TotalCount)> GetLogsByConditionAsync(
        Pagination pagination,
        Guid deviceId,
        string? level = null,
        string? keyword = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        // device_id 必填，先缩小查询范围。
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

        // 关键字筛选放在 device_id 之后，避免全表模糊匹配。
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions += " AND l.message LIKE @Keyword";
            parameters.Add("Keyword", $"%{keyword}%");
        }

        var dataSql = $@"
            SELECT l.id           AS Id,
                   l.device_id    AS DeviceId,
                   d.device_name  AS DeviceName,
                   l.level        AS Level,
                   l.message      AS Message,
                   l.log_time     AS LogTime,
                   l.received_at  AS ReceivedAt
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

        var items = (await connection.QueryAsync<DeviceLogListItemDto>(command)).ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(countCommand);

        return (items, totalCount);
    }
}
