using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper.Production.QueryServices.PassStation;
using IIoT.Dapper.Production.Repositories.PassStations;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.PassStations;

internal sealed class StackingPassStationSql :
    IPassStationWriteSql<StackingWriteModel>,
    IPassStationQuerySql<StackingPassListItemDto>,
    IPassStationQuerySql<StackingPassDetailDto>
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

    public string TableName => "pass_data_stacking";

    public string SelectColumns => """
        id AS Id,
        device_id AS DeviceId,
        barcode AS Barcode,
        tray_code AS TrayCode,
        sequence_no AS SequenceNo,
        layer_count AS LayerCount,
        cell_result AS CellResult,
        completed_time AS CompletedTime,
        received_at AS ReceivedAt
        """;
}
