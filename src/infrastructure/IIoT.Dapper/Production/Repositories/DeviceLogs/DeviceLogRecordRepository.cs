using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;

namespace IIoT.Dapper.Production.Repositories.DeviceLogs;

public sealed class DeviceLogRecordRepository(IDbConnectionFactory connectionFactory)
    : IDeviceLogRecordRepository
{
    public async Task InsertBatchAsync(
        IReadOnlyCollection<DeviceLogWriteModel> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return;

        const string sql = """
            insert into device_logs
            (
                id,
                device_id,
                mac_address,
                client_code,
                level,
                message,
                log_time,
                received_at
            )
            values
            (
                @Id,
                @DeviceId,
                @MacAddress,
                @ClientCode,
                @Level,
                @Message,
                @LogTime,
                @ReceivedAt
            );
            """;

        // 值对象在绑参一刻拆成扁平列,保持 SQL 视角干净
        var rows = items.Select(x => new
        {
            x.Id,
            x.DeviceId,
            MacAddress = x.Instance.MacAddress,
            ClientCode = x.Instance.ClientCode,
            x.Level,
            x.Message,
            x.LogTime,
            x.ReceivedAt
        });

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, rows, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}