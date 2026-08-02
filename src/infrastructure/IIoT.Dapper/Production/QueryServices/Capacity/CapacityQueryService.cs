using Dapper;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.Production.QueryServices.Capacity;

internal class CapacityQueryService(IDbConnectionFactory connectionFactory) : ICapacityQueryService
{
    private sealed class DailySummaryRow
    {
        public string ShiftCode { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int? OkCount { get; set; }
        public int? NgCount { get; set; }
    }
    // 指定设备某天的小时明细。

    public async Task<List<HourlyCapacityDto>> GetHourlyByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcCode = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = @"
            SELECT
                h.hour        AS Hour,
                h.minute      AS Minute,
                h.time_label  AS TimeLabel,
                h.shift_code  AS ShiftCode,
                h.total_count AS TotalCount,
                h.ok_count    AS OkCount,
                h.ng_count    AS NgCount,
                CASE WHEN h.plc_name_is_trusted THEN h.plc_name ELSE NULL END AS PlcName,
                h.plc_code    AS PlcCode
            FROM hourly_capacity h
            WHERE h.device_id = @DeviceId
              AND h.date = @Date
              AND (@PlcCode IS NULL OR h.plc_code = @PlcCode)
            ORDER BY h.hour, h.minute, h.plc_code";

        var cmd = new ReadOnlyCommandDefinition(
            sql,
            new { DeviceId = deviceId, Date = date, PlcCode = plcCode },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<HourlyCapacityDto>(cmd);
        return rows.ToList();
    }

    public async Task<List<HourlyCapacityPointDto>> GetHourlyRangeByDeviceIdAsync(
        Guid deviceId,
        DateTime startTime,
        DateTime endTime,
        string? plcCode = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                q.bucket_time AS Time,
                q.date        AS Date,
                q.hour        AS Hour,
                q.minute      AS Minute,
                q.time_label  AS TimeLabel,
                q.shift_code  AS ShiftCode,
                q.total_count AS TotalCount,
                q.ok_count    AS OkCount,
                q.ng_count    AS NgCount,
                q.plc_name    AS PlcName,
                q.plc_code    AS PlcCode
            FROM (
                SELECT
                    h.date,
                    h.hour,
                    h.minute,
                    h.time_label,
                    h.shift_code,
                    h.total_count,
                    h.ok_count,
                    h.ng_count,
                    h.plc_code,
                    CASE WHEN h.plc_name_is_trusted THEN h.plc_name ELSE NULL END AS plc_name,
                    (h.date::timestamp + pg_catalog.make_interval(
                        hours => h.hour::integer,
                        mins => h.minute::integer)) AS bucket_time
                FROM hourly_capacity h
                WHERE h.device_id = @DeviceId
                  AND h.date >= @StartDate
                  AND h.date <= @EndDate
                  AND (@PlcCode IS NULL OR h.plc_code = @PlcCode)
            ) q
            WHERE q.bucket_time >= @StartTime
              AND q.bucket_time <= @EndTime
            ORDER BY q.bucket_time ASC, q.plc_code ASC
            """;

        var cmd = new ReadOnlyCommandDefinition(
            sql,
            new
            {
                DeviceId = deviceId,
                StartDate = DateOnly.FromDateTime(startTime),
                EndDate = DateOnly.FromDateTime(endTime),
                StartTime = startTime,
                EndTime = endTime,
                PlcCode = plcCode
            },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<HourlyCapacityPointDto>(cmd);
        return rows.ToList();
    }

    public async Task<List<HourlyCapacityAggregateDto>> GetHourlyAggregateAsync(
        DateOnly date,
        Guid? processId = null,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds is { Count: 0 })
        {
            return [];
        }

        using var connection = connectionFactory.CreateConnection();

        var conditions = "WHERE h.date = @Date";
        var parameters = new DynamicParameters();
        parameters.Add("Date", date);

        if (processId.HasValue)
        {
            conditions += " AND d.process_id = @ProcessId";
            parameters.Add("ProcessId", processId.Value);
        }

        if (deviceIds is { Count: > 0 })
        {
            conditions += " AND h.device_id = ANY(@DeviceIds)";
            parameters.Add("DeviceIds", deviceIds.ToArray());
        }

        var sql = $@"
            SELECT
                h.hour                               AS Hour,
                h.minute                             AS Minute,
                pg_catalog.min(h.time_label::text) AS TimeLabel,
                COALESCE(pg_catalog.sum(h.total_count::integer), 0)::bigint AS TotalCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ok_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ok_count::integer), 0)::bigint END AS OkCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ng_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ng_count::integer), 0)::bigint END AS NgCount
            FROM hourly_capacity h
            INNER JOIN devices d ON h.device_id = d.id
            {conditions}
            GROUP BY h.hour, h.minute
            ORDER BY h.hour, h.minute";

        var cmd = new ReadOnlyCommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<HourlyCapacityAggregateDto>(cmd);
        return rows.ToList();
    }

    // 指定设备某天的白班/夜班汇总。

    public async Task<DailySummaryDto?> GetSummaryByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcCode = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = @"
            SELECT
                h.shift_code                    AS ShiftCode,
                COALESCE(pg_catalog.sum(h.total_count::integer), 0) AS TotalCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ok_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ok_count::integer), 0)::integer END AS OkCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ng_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ng_count::integer), 0)::integer END AS NgCount
            FROM hourly_capacity h
            WHERE h.device_id = @DeviceId
              AND h.date = @Date
              AND (@PlcCode IS NULL OR h.plc_code = @PlcCode)
            GROUP BY h.shift_code";

        var cmd = new ReadOnlyCommandDefinition(
            sql,
            new { DeviceId = deviceId, Date = date, PlcCode = plcCode },
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync<DailySummaryRow>(cmd)).ToList();
        if (rows.Count == 0) return null;

        return MergeSummaryRows(rows);
    }

    // 日期范围汇总的中间行模型。

    private sealed class DailyRangeRow
    {
        public DateOnly Date { get; set; }
        public string ShiftCode { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int? OkCount { get; set; }
        public int? NgCount { get; set; }
        public string? PlcCode { get; set; }
        public string? PlcName { get; set; }
    }

    public async Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        string? plcCode = null,
        CancellationToken cancellationToken = default,
        bool breakdownByPlc = false)
    {
        using var connection = connectionFactory.CreateConnection();

        var plcSelect = breakdownByPlc
            ? ", h.plc_code AS PlcCode, CASE WHEN pg_catalog.count(*) FILTER (WHERE NOT h.plc_name_is_trusted) > 0 THEN NULL ELSE pg_catalog.min(h.plc_name::text) END AS PlcName"
            : ", NULL::text AS PlcCode, NULL::text AS PlcName";
        var plcGroup = breakdownByPlc
            ? ", h.plc_code"
            : string.Empty;
        var plcOrder = breakdownByPlc
            ? ", h.plc_code ASC"
            : string.Empty;
        var sql = $@"
            SELECT
                h.date                          AS Date,
                h.shift_code                    AS ShiftCode,
                COALESCE(pg_catalog.sum(h.total_count::integer), 0) AS TotalCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ok_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ok_count::integer), 0)::integer END AS OkCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ng_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ng_count::integer), 0)::integer END AS NgCount
                {plcSelect}
            FROM hourly_capacity h
            WHERE h.device_id = @DeviceId
              AND h.date >= @StartDate
              AND h.date <= @EndDate
              AND (@PlcCode IS NULL OR h.plc_code = @PlcCode)
            GROUP BY h.date, h.shift_code{plcGroup}
            ORDER BY h.date ASC{plcOrder}, h.shift_code ASC";

        var cmd = new ReadOnlyCommandDefinition(
            sql,
            new { DeviceId = deviceId, StartDate = startDate, EndDate = endDate, PlcCode = plcCode },
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync<DailyRangeRow>(cmd)).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var result = rows
            .GroupBy(r => (
                r.Date,
                PlcCode: breakdownByPlc ? r.PlcCode : null,
                PlcName: breakdownByPlc ? r.PlcName : null))
            .Select(g =>
            {
                var day = g.FirstOrDefault(x => x.ShiftCode.Equals("D", StringComparison.OrdinalIgnoreCase));
                var night = g.FirstOrDefault(x => x.ShiftCode.Equals("N", StringComparison.OrdinalIgnoreCase));

                var dayTotal = day?.TotalCount ?? 0;
                var dayOk = day is null ? 0 : day.OkCount;
                var dayNg = day is null ? 0 : day.NgCount;

                var nightTotal = night?.TotalCount ?? 0;
                var nightOk = night is null ? 0 : night.OkCount;
                var nightNg = night is null ? 0 : night.NgCount;

                return new DailyRangeSummaryDto(
                    Date: g.Key.Date,
                    TotalCount: dayTotal + nightTotal,
                    OkCount: AddQuality(dayOk, nightOk),
                    NgCount: AddQuality(dayNg, nightNg),
                    DayShiftTotal: dayTotal,
                    DayShiftOk: dayOk,
                    DayShiftNg: dayNg,
                    NightShiftTotal: nightTotal,
                    NightShiftOk: nightOk,
                    NightShiftNg: nightNg,
                    PlcName: g.Key.PlcName,
                    PlcCode: g.Key.PlcCode
                );
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.PlcCode, StringComparer.Ordinal)
            .ToList();

        return result;
    }

    // 后台分页列表不按 plcName 拆分，展示设备当天总量。

    public async Task<(List<DailyCapacityPagedItemDto> Items, int TotalCount)> GetDailyPagedAsync(
        Pagination pagination,
        DateOnly? date = null,
        Guid? deviceId = null,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var conditions = "WHERE 1=1";
        var parameters = new DynamicParameters();

        if (date.HasValue)
        {
            conditions += " AND h.date = @Date";
            parameters.Add("Date", date.Value);
        }

        if (deviceId.HasValue)
        {
            conditions += " AND h.device_id = @DeviceId";
            parameters.Add("DeviceId", deviceId.Value);
        }

        if (deviceIds is { Count: > 0 })
        {
            conditions += " AND h.device_id = ANY(@DeviceIds)";
            parameters.Add("DeviceIds", deviceIds.ToArray());
        }

        var dataSql = $@"
            SELECT
                h.device_id    AS DeviceId,
                d.device_name  AS DeviceName,
                h.date         AS Date,
                COALESCE(pg_catalog.sum(h.total_count::integer), 0) AS TotalCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ok_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ok_count::integer), 0)::bigint END AS OkCount,
                CASE WHEN pg_catalog.count(*) FILTER (WHERE h.ng_count IS NULL) > 0
                    THEN NULL ELSE COALESCE(pg_catalog.sum(h.ng_count::integer), 0)::bigint END AS NgCount,
                pg_catalog.max(h.reported_at::timestamptz) AS ReportedAt
            FROM hourly_capacity h
            INNER JOIN devices d ON h.device_id = d.id
            {conditions}
            GROUP BY h.device_id, d.device_name, h.date
            ORDER BY h.date DESC, d.device_name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $@"
            SELECT pg_catalog.count(*) FROM (
                SELECT h.device_id, h.date
                FROM hourly_capacity h
                {conditions}
                GROUP BY h.device_id, h.date
            ) AS sub";

        var offset = (pagination.PageNumber - 1) * pagination.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pagination.PageSize);

        var dataCmd = new ReadOnlyCommandDefinition(
            dataSql,
            parameters,
            cancellationToken: cancellationToken);
        var countCmd = new ReadOnlyCommandDefinition(
            countSql,
            parameters,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<DailyCapacityPagedRow>(dataCmd);
        var items = rows
            .Select(row => new DailyCapacityPagedItemDto(
                row.DeviceId,
                row.DeviceName,
                row.Date,
                row.TotalCount,
                row.OkCount,
                row.NgCount,
                row.TotalCount > 0 && row.OkCount.HasValue
                    ? decimal.Round(
                        row.OkCount.Value * 100m / row.TotalCount,
                        2,
                        MidpointRounding.AwayFromZero)
                    : null,
                row.ReportedAt))
            .ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(countCmd);

        return (items, totalCount);
    }

    private sealed class DailyCapacityPagedRow
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public long TotalCount { get; set; }
        public long? OkCount { get; set; }
        public long? NgCount { get; set; }
        public DateTime ReportedAt { get; set; }
    }

    // 合并白班/夜班汇总结果。

    private static DailySummaryDto MergeSummaryRows(List<DailySummaryRow> rows)
    {
        int dayTotal = 0, nightTotal = 0;
        int? dayOk = 0, dayNg = 0, nightOk = 0, nightNg = 0;

        foreach (var row in rows)
        {
            var shift = row.ShiftCode ?? string.Empty;
            var t = row.TotalCount;
            var o = row.OkCount;
            var n = row.NgCount;

            if (shift.Equals("D", StringComparison.OrdinalIgnoreCase))
            { dayTotal = t; dayOk = o; dayNg = n; }
            else
            { nightTotal = t; nightOk = o; nightNg = n; }
        }

        return new DailySummaryDto(
            TotalCount: dayTotal + nightTotal,
            OkCount: AddQuality(dayOk, nightOk),
            NgCount: AddQuality(dayNg, nightNg),
            DayShiftTotal: dayTotal,
            DayShiftOk: dayOk,
            DayShiftNg: dayNg,
            NightShiftTotal: nightTotal,
            NightShiftOk: nightOk,
            NightShiftNg: nightNg
        );
    }

    private static int? AddQuality(int? left, int? right)
        => left.HasValue && right.HasValue ? left.Value + right.Value : null;
}
