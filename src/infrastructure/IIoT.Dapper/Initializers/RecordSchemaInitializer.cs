using Dapper;
using Microsoft.Extensions.Logging;

namespace IIoT.Dapper.Initializers;

/// <summary>
/// 记录表 schema 初始化器。
/// 
/// 从输出目录下的 Production/Sql/Schemas/*.sql 按文件名顺序执行脚本,
/// 由 MigrationWorkApp 在启动时显式调用一次。
/// 
/// 与 EF Core Migration 的分工:
///   - EF Core 负责聚合根的 schema (Device / Recipe / Employee / MfgProcess / Identity)
///   - 本初始化器负责记录类的 schema (device_logs / hourly_capacity / pass_data_injection ...)
/// 两者边界由 DDD 分层决定,物理上互不相交。
/// </summary>
public sealed class RecordSchemaInitializer(
    IDbConnectionFactory connectionFactory,
    ILogger<RecordSchemaInitializer> logger)
{
    private const string SchemaRelativePath = "Production/Sql/Schemas";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, SchemaRelativePath);

        if (!Directory.Exists(schemaDirectory))
        {
            logger.LogWarning("记录表 Schema 目录不存在: {Directory}", schemaDirectory);
            return;
        }

        var scriptFiles = Directory
            .GetFiles(schemaDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scriptFiles.Count == 0)
        {
            logger.LogWarning("记录表 Schema 目录为空: {Directory}", schemaDirectory);
            return;
        }

        logger.LogInformation("发现 {Count} 个 Schema 脚本,开始顺序执行。", scriptFiles.Count);

        using var connection = connectionFactory.CreateConnection();

        foreach (var scriptPath in scriptFiles)
        {
            var fileName = Path.GetFileName(scriptPath);
            var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);

            if (string.IsNullOrWhiteSpace(sql))
            {
                logger.LogWarning("Schema 脚本为空,跳过: {FileName}", fileName);
                continue;
            }

            logger.LogInformation("执行 Schema 脚本: {FileName}", fileName);
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
            logger.LogInformation("Schema 脚本执行完成: {FileName}", fileName);
        }

        logger.LogInformation("记录表 Schema 初始化全部完成。");
    }
}