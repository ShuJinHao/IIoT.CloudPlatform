using Dapper;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Paging;

namespace IIoT.Dapper.Production.QueryServices.Capacity;

public class CapacityQueryService(IDbConnectionFactory connectionFactory) : ICapacityQueryService
{
    // ── 1. 按日半小时明细 ────────────────────────────────────────────────

    public async Task<List<HourlyCapacityDto>> GetHourlyByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcName = null,
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
                h.ng_count    AS NgCount
            FROM hourly_capacity h
            WHERE h.device_id = @DeviceId
              AND h.date = @Date
              AND (@PlcName IS NULL OR h.plc_name = @PlcName)
            ORDER BY h.hour, h.minute";

        var cmd = new CommandDefinition(
            sql,
            new { DeviceId = deviceId, Date = date, PlcName = plcName },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<HourlyCapacityDto>(cmd);
        return rows.ToList();
    }

    // ── 2. 按日汇总（白班+夜班合并）──────────────────────────────────────

    public async Task<DailySummaryDto?> GetSummaryByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = @"
            SELECT
                h.shift_code                    AS ShiftCode,
                COALESCE(SUM(h.total_count), 0) AS TotalCount,
                COALESCE(SUM(h.ok_count),    0) AS OkCount,
                COALESCE(SUM(h.ng_count),    0) AS NgCount
            FROM hourly_capacity h
            WHERE h.device_id = @DeviceId
              AND h.date = @Date
              AND (@PlcName IS NULL OR h.plc_name = @PlcName)
            GROUP BY h.shift_code";

        var cmd = new CommandDefinition(
            sql,
            new { DeviceId = deviceId, Date = date, PlcName = plcName },
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync(cmd)).ToList();
        if (rows.Count == 0) return null;

        return MergeSummaryRows(rows);
    }

    // ── 3. 按日期范围汇总（月/年查询，一次请求替代循环N次）──────────────

    private sealed class DailyRangeRow
    {
        public DateOnly Date { get; set; }
        public string ShiftCode { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int OkCount { get; set; }
        public int NgCount { get; set; }
    }

    public async Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = @"
            SELECT
                h.date                          AS Date,
                h.shift_code                    AS ShiftCode,
                COALESCE(SUM(h.total_count), 0) AS TotalCount,
                COALESCE(SUM(h.ok_count),    0) AS OkCount,
                COALESCE(SUM(h.ng_count),    0) AS NgCount
            FROM hourly_capacity h
            WHERE h.device_id = @DeviceId
              AND h.date >= @StartDate
              AND h.date <= @EndDate
              AND (@PlcName IS NULL OR h.plc_name = @PlcName)
            GROUP BY h.date, h.shift_code
            ORDER BY h.date ASC, h.shift_code ASC";

        var cmd = new CommandDefinition(
            sql,
            new { DeviceId = deviceId, StartDate = startDate, EndDate = endDate, PlcName = plcName },
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync<DailyRangeRow>(cmd)).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var result = rows
            .GroupBy(r => r.Date)
            .Select(g =>
            {
                var day = g.FirstOrDefault(x => x.ShiftCode.Equals("D", StringComparison.OrdinalIgnoreCase));
                var night = g.FirstOrDefault(x => x.ShiftCode.Equals("N", StringComparison.OrdinalIgnoreCase));

                var dayTotal = day?.TotalCount ?? 0;
                var dayOk = day?.OkCount ?? 0;
                var dayNg = day?.NgCount ?? 0;

                var nightTotal = night?.TotalCount ?? 0;
                var nightOk = night?.OkCount ?? 0;
                var nightNg = night?.NgCount ?? 0;

                return new DailyRangeSummaryDto(
                    Date: g.Key,
                    TotalCount: dayTotal + nightTotal,
                    OkCount: dayOk + nightOk,
                    NgCount: dayNg + nightNg,
                    DayShiftTotal: dayTotal,
                    DayShiftOk: dayOk,
                    DayShiftNg: dayNg,
                    NightShiftTotal: nightTotal,
                    NightShiftOk: nightOk,
                    NightShiftNg: nightNg
                );
            })
            .OrderBy(x => x.Date)
            .ToList();

        return result;
    }

    // ── 4. 云端后台分页（不加 plcName 过滤，后台管理用途，展示全量数据）──

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
            conditions += " AND h.date = @Date";
            parameters.Add("Date", date.Value);
        }

        if (deviceId.HasValue)
        {
            conditions += " AND h.device_id = @DeviceId";
            parameters.Add("DeviceId", deviceId.Value);
        }

        var dataSql = $@"
            SELECT
                h.device_id,
                d.device_name,
                h.date,
                COALESCE(SUM(h.total_count), 0) AS total_count,
                COALESCE(SUM(h.ok_count),    0) AS ok_count,
                COALESCE(SUM(h.ng_count),    0) AS ng_count,
                CASE WHEN SUM(h.total_count) > 0
                     THEN ROUND(SUM(h.ok_count) * 100.0 / SUM(h.total_count), 2)
                     ELSE 0 END AS ok_rate,
                MAX(h.reported_at) AS reported_at
            FROM hourly_capacity h
            INNER JOIN devices d ON h.device_id = d.id
            {conditions}
            GROUP BY h.device_id, d.device_name, h.date
            ORDER BY h.date DESC, d.device_name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var countSql = $@"
            SELECT COUNT(*) FROM (
                SELECT h.device_id, h.date
                FROM hourly_capacity h
                {conditions}
                GROUP BY h.device_id, h.date
            ) AS sub";

        var offset = (pagination.PageNumber - 1) * pagination.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pagination.PageSize);

        var dataCmd = new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken);
        var countCmd = new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken);

        var items = (await connection.QueryAsync(dataCmd)).ToList();
        var totalCount = await connection.ExecuteScalarAsync<int>(countCmd);

        return (items, totalCount);
    }

    // ── 私有辅助 ─────────────────────────────────────────────────────────

    private static DailySummaryDto MergeSummaryRows(List<dynamic> rows)
    {
        int dayTotal = 0, dayOk = 0, dayNg = 0;
        int nightTotal = 0, nightOk = 0, nightNg = 0;

        foreach (var row in rows)
        {
            string shift = (string?)row.ShiftCode ?? "";
            int t = (int)(row.TotalCount ?? 0);
            int o = (int)(row.OkCount ?? 0);
            int n = (int)(row.NgCount ?? 0);

            if (shift.Equals("D", StringComparison.OrdinalIgnoreCase))
            { dayTotal = t; dayOk = o; dayNg = n; }
            else
            { nightTotal = t; nightOk = o; nightNg = n; }
        }

        return new DailySummaryDto(
            TotalCount: dayTotal + nightTotal,
            OkCount: dayOk + nightOk,
            NgCount: dayNg + nightNg,
            DayShiftTotal: dayTotal,
            DayShiftOk: dayOk,
            DayShiftNg: dayNg,
            NightShiftTotal: nightTotal,
            NightShiftOk: nightOk,
            NightShiftNg: nightNg
        );
    }
}