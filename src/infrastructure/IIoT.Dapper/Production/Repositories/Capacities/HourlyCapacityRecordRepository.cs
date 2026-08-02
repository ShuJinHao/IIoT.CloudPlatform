using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;

namespace IIoT.Dapper.Production.Repositories.Capacities;

internal sealed class HourlyCapacityRecordRepository(IDbConnectionFactory connectionFactory)
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
                date,
                shift_code,
                hour,
                minute,
                time_label,
                total_count,
                ok_count,
                ng_count,
                schema_version,
                process_type,
                plc_code,
                plc_name,
                plc_name_is_trusted,
                reported_at
            )
            values
            (
                @Id,
                @DeviceId,
                @Date,
                @ShiftCode,
                @Hour,
                @Minute,
                @TimeLabel,
                @TotalCount,
                @OkCount,
                @NgCount,
                @SchemaVersion,
                @ProcessType,
                @PlcCode,
                @PlcName,
                @PlcNameIsTrusted,
                @ReportedAt
            )
            on conflict (device_id, date, shift_code, hour, minute, plc_code)
            do update set
                time_label = excluded.time_label,
                total_count = excluded.total_count,
                ok_count = case
                    when excluded.schema_version >= hourly_capacity.schema_version then excluded.ok_count
                    else hourly_capacity.ok_count
                end,
                ng_count = case
                    when excluded.schema_version >= hourly_capacity.schema_version then excluded.ng_count
                    else hourly_capacity.ng_count
                end,
                schema_version = greatest(hourly_capacity.schema_version, excluded.schema_version),
                process_type = coalesce(excluded.process_type, hourly_capacity.process_type),
                plc_name = case
                    when excluded.plc_name_is_trusted then excluded.plc_name
                    when hourly_capacity.plc_name_is_trusted then hourly_capacity.plc_name
                    else excluded.plc_name
                end,
                plc_name_is_trusted = hourly_capacity.plc_name_is_trusted or excluded.plc_name_is_trusted,
                reported_at = excluded.reported_at
            where hourly_capacity.total_count < excluded.total_count
               or (
                    hourly_capacity.total_count = excluded.total_count
                    and hourly_capacity.reported_at <= excluded.reported_at
               );
            """;

        var row = new
        {
            item.Id,
            item.DeviceId,
            item.Date,
            item.ShiftCode,
            item.Hour,
            item.Minute,
            item.TimeLabel,
            item.TotalCount,
            item.OkCount,
            item.NgCount,
            item.SchemaVersion,
            item.ProcessType,
            item.PlcCode,
            PlcName = item.PlcName ?? string.Empty,
            item.PlcNameIsTrusted,
            item.ReportedAt
        };

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, row, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
