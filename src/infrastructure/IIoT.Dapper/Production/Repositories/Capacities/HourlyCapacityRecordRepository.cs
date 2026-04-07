using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;

namespace IIoT.Dapper.Production.Repositories.Capacities;

public sealed class HourlyCapacityRecordRepository(IDbConnectionFactory connectionFactory)
    : IHourlyCapacityRecordRepository
{
    public async Task UpsertAsync(
        HourlyCapacityWriteModel item,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            insert into hourly_capacity
            (
                id,
                device_id,
                mac_address,
                client_code,
                date,
                shift_code,
                hour,
                minute,
                time_label,
                total_count,
                ok_count,
                ng_count,
                plc_name,
                reported_at
            )
            values
            (
                @Id,
                @DeviceId,
                @MacAddress,
                @ClientCode,
                @Date,
                @ShiftCode,
                @Hour,
                @Minute,
                @TimeLabel,
                @TotalCount,
                @OkCount,
                @NgCount,
                @PlcName,
                @ReportedAt
            )
            on conflict (mac_address, client_code, date, shift_code, hour, minute, plc_name)
            do update set
                device_id = excluded.device_id,
                time_label = excluded.time_label,
                total_count = excluded.total_count,
                ok_count = excluded.ok_count,
                ng_count = excluded.ng_count,
                reported_at = excluded.reported_at;
            """;

        // 值对象拆列 + PlcName 空值兜底(避开 PostgreSQL 唯一索引对 NULL 的"NULL 不等于 NULL"陷阱)
        var row = new
        {
            item.Id,
            item.DeviceId,
            MacAddress = item.Instance.MacAddress,
            ClientCode = item.Instance.ClientCode,
            item.Date,
            item.ShiftCode,
            item.Hour,
            item.Minute,
            item.TimeLabel,
            item.TotalCount,
            item.OkCount,
            item.NgCount,
            PlcName = item.PlcName ?? string.Empty,
            item.ReportedAt
        };

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, row, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}