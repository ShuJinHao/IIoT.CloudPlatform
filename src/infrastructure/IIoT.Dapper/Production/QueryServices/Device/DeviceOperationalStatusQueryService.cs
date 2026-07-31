using Dapper;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.Device;

internal sealed class DeviceOperationalStatusQueryService(IDbConnectionFactory connectionFactory)
    : IDeviceOperationalStatusQueryService
{
    public async Task<DeviceStatusSummaryDto> GetStatusSummaryAsync(
        DateTimeOffset offlineCutoff,
        DateTimeOffset statusWindowStart,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds is { Count: 0 })
        {
            return new DeviceStatusSummaryDto(0, 0, 0, 0, 0, DateTimeOffset.UtcNow);
        }

        using var connection = connectionFactory.CreateConnection();

        var deviceConditions = "WHERE 1=1";
        var parameters = new DynamicParameters();
        parameters.Add("OfflineCutoff", offlineCutoff);
        parameters.Add("StatusWindowStart", statusWindowStart);
        var generatedAt = DateTime.UtcNow;
        parameters.Add("GeneratedAt", generatedAt);

        if (deviceIds is { Count: > 0 })
        {
            deviceConditions += " AND d.id = ANY(@DeviceIds)";
            parameters.Add("DeviceIds", deviceIds.ToArray());
        }

        var sql = $@"
            WITH scoped_devices AS (
                SELECT d.id
                FROM devices d
                {deviceConditions}
            ),
            last_activity AS (
                SELECT
                    r.device_id,
                    pg_catalog.max(r.last_seen_at_utc::timestamptz) AS last_seen_at_utc
                FROM upload_receive_registrations r
                INNER JOIN scoped_devices sd ON sd.id = r.device_id
                GROUP BY r.device_id
            ),
            recent_levels AS (
                SELECT
                    l.device_id,
                    CASE WHEN pg_catalog.count(*) FILTER (
                        WHERE pg_catalog.upper(l.level::text) IN ('ERROR', 'ERR')) > 0
                        THEN 1 ELSE 0 END AS has_error,
                    CASE WHEN pg_catalog.count(*) FILTER (
                        WHERE pg_catalog.upper(l.level::text) IN ('WARN', 'WARNING')) > 0
                        THEN 1 ELSE 0 END AS has_warning
                FROM device_logs l
                INNER JOIN scoped_devices sd ON sd.id = l.device_id
                WHERE l.log_time >= @StatusWindowStart
                GROUP BY l.device_id
            ),
            status_rows AS (
                SELECT
                    CASE
                        WHEN la.last_seen_at_utc IS NULL OR la.last_seen_at_utc < @OfflineCutoff THEN 'Offline'
                        WHEN COALESCE(rl.has_error, 0) = 1 THEN 'Error'
                        WHEN COALESCE(rl.has_warning, 0) = 1 THEN 'Warning'
                        ELSE 'Online'
                    END AS status
                FROM scoped_devices sd
                LEFT JOIN last_activity la ON la.device_id = sd.id
                LEFT JOIN recent_levels rl ON rl.device_id = sd.id
            )
            SELECT
                pg_catalog.count(*)::int AS Total,
                pg_catalog.count(*) FILTER (WHERE status = 'Online')::int AS Online,
                pg_catalog.count(*) FILTER (WHERE status = 'Warning')::int AS Warning,
                pg_catalog.count(*) FILTER (WHERE status = 'Error')::int AS Error,
                pg_catalog.count(*) FILTER (WHERE status = 'Offline')::int AS Offline,
                @GeneratedAt AS GeneratedAt
            FROM status_rows";

        var command = new ReadOnlyCommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken);
        var row = await connection.QuerySingleAsync<DeviceStatusSummaryRow>(command);

        return new DeviceStatusSummaryDto(
            row.Total,
            row.Online,
            row.Warning,
            row.Error,
            row.Offline,
            new DateTimeOffset(DateTime.SpecifyKind(row.GeneratedAt, DateTimeKind.Utc)));
    }

    private sealed record DeviceStatusSummaryRow(
        int Total,
        int Online,
        int Warning,
        int Error,
        int Offline,
        DateTime GeneratedAt);
}
