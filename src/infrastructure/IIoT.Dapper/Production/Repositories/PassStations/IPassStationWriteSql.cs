using IIoT.Core.Production.Contracts.PassStation;

namespace IIoT.Dapper.Production.Repositories.PassStations;

internal interface IPassStationWriteSql<TWriteModel>
    where TWriteModel : IPassStationWriteModel
{
    string InsertSql { get; }
}
