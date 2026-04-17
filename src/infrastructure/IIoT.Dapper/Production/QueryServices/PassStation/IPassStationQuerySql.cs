namespace IIoT.Dapper.Production.QueryServices.PassStation;

internal interface IPassStationQuerySql<TDto>
{
    string TableName { get; }

    string SelectColumns { get; }
}
