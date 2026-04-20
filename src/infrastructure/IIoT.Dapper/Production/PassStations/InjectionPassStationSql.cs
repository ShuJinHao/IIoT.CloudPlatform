using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper.Production.QueryServices.PassStation;
using IIoT.Dapper.Production.Repositories.PassStations;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.PassStations;

internal sealed class InjectionPassStationSql :
    IPassStationWriteSql<InjectionWriteModel>,
    IPassStationQuerySql<InjectionPassListItemDto>,
    IPassStationQuerySql<InjectionPassDetailDto>
{
    public string InsertSql => """
        insert into pass_data_injection
        (
            id, device_id, cell_result,
            completed_time, received_at, barcode,
            pre_injection_time, pre_injection_weight,
            post_injection_time, post_injection_weight, injection_volume
        )
        values
        (
            @Id, @DeviceId, @CellResult,
            @CompletedTime, @ReceivedAt, @Barcode,
            @PreInjectionTime, @PreInjectionWeight,
            @PostInjectionTime, @PostInjectionWeight, @InjectionVolume
        )
        on conflict (device_id, barcode, completed_time) do nothing;
        """;

    public string TableName => "pass_data_injection";

    public string SelectColumns => """
        id AS Id,
        device_id AS DeviceId,
        barcode AS Barcode,
        cell_result AS CellResult,
        pre_injection_time AS PreInjectionTime,
        pre_injection_weight AS PreInjectionWeight,
        post_injection_time AS PostInjectionTime,
        post_injection_weight AS PostInjectionWeight,
        injection_volume AS InjectionVolume,
        completed_time AS CompletedTime,
        received_at AS ReceivedAt
        """;
}
