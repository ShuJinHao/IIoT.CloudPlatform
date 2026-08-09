using Dapper;
using IIoT.Core.Production.Contracts.PassStation;

namespace IIoT.Dapper.Production.Repositories.PassStations;

internal sealed class PassStationRecordRepository(IDbConnectionFactory connectionFactory)
    : IPassStationRecordRepository
{
    private const string InsertSql = """
        with completion_claim as
        (
            insert into pass_station_completion_claims
            (
                device_id, type_key, completion_id
            )
            select @DeviceId, @TypeKey, @CompletionId
            where @CompletionId is not null
            on conflict do nothing
            returning completion_id
        )
        insert into pass_station_records
        (
            id, device_id, type_key, barcode, cell_result,
            completed_time, received_at, completion_id, deduplication_key, payload_jsonb
        )
        select
            @Id, @DeviceId, @TypeKey, @Barcode, @CellResult,
            @CompletedTime, @ReceivedAt, @CompletionId, @DeduplicationKey, cast(@PayloadJson as jsonb)
        where @CompletionId is null
           or exists (select 1 from completion_claim)
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
