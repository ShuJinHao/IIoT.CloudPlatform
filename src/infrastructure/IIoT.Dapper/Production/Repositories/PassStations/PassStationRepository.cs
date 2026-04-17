using Dapper;
using IIoT.Core.Production.Contracts.PassStation;

namespace IIoT.Dapper.Production.Repositories.PassStations;

internal sealed class PassStationRepository<TWriteModel>(
    IDbConnectionFactory connectionFactory,
    IPassStationWriteSql<TWriteModel> sql)
    : IPassStationRepository<TWriteModel>
    where TWriteModel : IPassStationWriteModel
{
    public async Task InsertBatchAsync(
        IReadOnlyCollection<TWriteModel> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return;

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql.InsertSql, items, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
