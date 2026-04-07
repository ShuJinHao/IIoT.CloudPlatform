using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;

namespace IIoT.Dapper.Initializers;

public sealed class RecordSchemaInitializer(
    IDbConnectionFactory connectionFactory,
    ILogger<RecordSchemaInitializer> logger)
{
    private const string SchemaResourcePrefix = "IIoT.Dapper.Sql.Schemas.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith(SchemaResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .ToList();

        if (resourceNames.Count == 0)
        {
            logger.LogWarning("未找到任何记录表 Schema 脚本。");
            return;
        }

        using var connection = connectionFactory.CreateConnection();

        foreach (var resourceName in resourceNames)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                logger.LogWarning("无法读取 Schema 资源：{ResourceName}", resourceName);
                continue;
            }

            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(sql))
            {
                logger.LogWarning("Schema 脚本为空：{ResourceName}", resourceName);
                continue;
            }

            logger.LogInformation("开始执行 Schema 脚本：{ResourceName}", resourceName);
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
            logger.LogInformation("Schema 脚本执行完成：{ResourceName}", resourceName);
        }
    }
}