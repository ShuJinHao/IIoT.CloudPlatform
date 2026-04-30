using Dapper;
using IIoT.Core.Production.Contracts.PassStation;

namespace IIoT.Dapper.Production.Repositories.PassStations;

public sealed class PassStationRecordRepository(IDbConnectionFactory connectionFactory)
    : IPassStationRecordRepository
{
    private const string InsertSql = """
        insert into pass_station_records
        (
            id, device_id, type_key, barcode, cell_result,
            completed_time, received_at, deduplication_key, payload_jsonb
        )
        values
        (
            @Id, @DeviceId, @TypeKey, @Barcode, @CellResult,
            @CompletedTime, @ReceivedAt, @DeduplicationKey, cast(@PayloadJson as jsonb)
        )
        on conflict (type_key, deduplication_key, completed_time) do nothing;
        """;

    public async Task InsertBatchAsync(
        IReadOnlyCollection<PassStationRecordWriteModel> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return;

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            InsertSql,
            items,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
