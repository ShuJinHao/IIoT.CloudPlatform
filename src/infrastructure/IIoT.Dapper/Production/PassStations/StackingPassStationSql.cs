using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper.Production.Repositories.PassStations;

namespace IIoT.Dapper.Production.PassStations;

internal sealed class StackingPassStationSql : IPassStationWriteSql<StackingWriteModel>
{
    public string InsertSql => """
        insert into pass_data_stacking
        (
            id, device_id, barcode, tray_code,
            sequence_no, layer_count, cell_result,
            completed_time, received_at
        )
        values
        (
            @Id, @DeviceId, @Barcode, @TrayCode,
            @SequenceNo, @LayerCount, @CellResult,
            @CompletedTime, @ReceivedAt
        )
        on conflict (device_id, barcode, completed_time) do nothing;
        """;
}
