using Dapper;
using IIoT.Services.Common.Contracts.DapperQueries;

namespace IIoT.Dapper.Production.QueryServices.MfgProcess;

/// <summary>
/// 跨聚合的工序占用情况查询服务 — Dapper 实现。
///
/// 直接读 devices / recipes 这两张 EF Core 写入侧的聚合根表,
/// 走索引 + LIMIT 1 + ExecuteScalar,避免任何聚合根物化开销。
/// 不会破坏 DDD 分层 — 查询(读)路径允许跨越聚合根边界,
/// 写入(命令)路径仍然必须经过 EF Core Repository。
/// </summary>
public class ProcessUsageQueryService(IDbConnectionFactory connectionFactory)
    : IProcessUsageQueryService
{
    public async Task<bool> HasDeviceUnderProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 1
            FROM devices
            WHERE process_id = @ProcessId
            LIMIT 1
            """;

        using var connection = connectionFactory.CreateConnection();

        var hit = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                sql,
                new { ProcessId = processId },
                cancellationToken: cancellationToken));

        return hit.HasValue;
    }

    public async Task<bool> HasRecipeUnderProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 1
            FROM recipes
            WHERE process_id = @ProcessId
            LIMIT 1
            """;

        using var connection = connectionFactory.CreateConnection();

        var hit = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                sql,
                new { ProcessId = processId },
                cancellationToken: cancellationToken));

        return hit.HasValue;
    }
}