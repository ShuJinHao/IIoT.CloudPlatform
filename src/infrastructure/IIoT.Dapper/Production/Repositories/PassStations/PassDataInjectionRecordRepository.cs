using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;

namespace IIoT.Dapper.Production.Repositories.PassStations;

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
            )
            on conflict (device_id, barcode, completed_time) do nothing;
            """;

        var row = new
        {
            item.Id,
            item.DeviceId,
            MacAddress = item.Instance.MacAddress,
            ClientCode = item.Instance.ClientCode,
            item.CellResult,
            item.CompletedTime,
            item.ReceivedAt,
            item.Barcode,
            item.PreInjectionTime,
            item.PreInjectionWeight,
            item.PostInjectionTime,
            item.PostInjectionWeight,
            item.InjectionVolume
        };

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, row, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}