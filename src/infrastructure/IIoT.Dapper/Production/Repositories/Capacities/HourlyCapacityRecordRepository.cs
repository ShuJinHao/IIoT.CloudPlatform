using Dapper;
using IIoT.Services.Common.Contracts.RecordRepositories;

namespace IIoT.Dapper.Repositories.Capacities;

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

        var normalized = item with
        {
            PlcName = item.PlcName ?? string.Empty
        };

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, normalized, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}