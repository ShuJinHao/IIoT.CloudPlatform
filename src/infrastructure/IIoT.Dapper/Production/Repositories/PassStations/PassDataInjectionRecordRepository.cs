using Dapper;
using IIoT.Services.Common.Contracts.RecordRepositories;

namespace IIoT.Dapper.Repositories.PassStations;

public sealed class PassDataInjectionRecordRepository(IDbConnectionFactory connectionFactory)
    : IPassDataInjectionRecordRepository
{
    public async Task InsertAsync(
        PassDataInjectionWriteModel item,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            insert into pass_data_injection
            (
                id,
                device_id,
                mac_address,
                client_code,
                cell_result,
                completed_time,
                received_at,
                barcode,
                pre_injection_time,
                pre_injection_weight,
                post_injection_time,
                post_injection_weight,
                injection_volume
            )
            values
            (
                @Id,
                @DeviceId,
                @MacAddress,
                @ClientCode,
                @CellResult,
                @CompletedTime,
                @ReceivedAt,
                @Barcode,
                @PreInjectionTime,
                @PreInjectionWeight,
                @PostInjectionTime,
                @PostInjectionWeight,
                @InjectionVolume
            );
            """;

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, item, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}