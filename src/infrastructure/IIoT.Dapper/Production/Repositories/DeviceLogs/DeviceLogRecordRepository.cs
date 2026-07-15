using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;

namespace IIoT.Dapper.Production.Repositories.DeviceLogs;

internal sealed class DeviceLogRecordRepository(IDbConnectionFactory connectionFactory)
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
                level,
                message,
                log_time,
                received_at,
                idempotency_key
            )
            values
            (
                @Id,
                @DeviceId,
                @Level,
                @Message,
                @LogTime,
                @ReceivedAt,
                @IdempotencyKey
            )
            on conflict (device_id, log_time, idempotency_key)
                where idempotency_key is not null
            do nothing;
            """;

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, items, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
