using Dapper;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using System.Data;

namespace IIoT.Dapper.Production.QueryServices.Device;

public sealed class DeviceDeletionDependencyQueryService(IDbConnectionFactory connectionFactory)
    : IDeviceDeletionDependencyQueryService
{
    public async Task<DeviceDeletionDependencies> GetDependenciesAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                exists(select 1 from recipes where device_id = @DeviceId)            as HasRecipes,
                exists(select 1 from hourly_capacity where device_id = @DeviceId)    as HasCapacities,
                exists(select 1 from device_logs where device_id = @DeviceId)        as HasDeviceLogs,
                exists(select 1 from pass_station_records where device_id = @DeviceId) as HasPassStations;
            """;

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<DeviceDeletionDependencies>(command);
    }

    public async Task<DeviceDeletionImpact> GetImpactAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await GetImpactAsync(connection, transaction: null, deviceId, cancellationToken);
    }

    public async Task<DeviceCascadeDeletionResult> DeleteCascadeAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            var impact = await GetImpactAsync(connection, transaction, deviceId, cancellationToken);

            await CountDeletedByReturningAsync(
                connection,
                transaction,
                """
                with deleted as (
                    delete from edge_device_client_plugin_versions plugin
                    using edge_device_client_version_snapshots snapshot
                    where plugin.device_client_version_snapshot_id = snapshot.id
                      and snapshot.device_id = @DeviceId
                    returning 1
                )
                select count(*) from deleted;
                """,
                deviceId,
                cancellationToken);

            await DeleteRowsAsync(connection, transaction, "delete from edge_device_client_version_snapshots where device_id = @DeviceId;", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, "delete from upload_receive_registrations where device_id = @DeviceId;", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, "delete from employee_device_accesses where device_id = @DeviceId;", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, """delete from refresh_token_sessions where "ActorType" = @ActorType and "SubjectId" = @DeviceId;""", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, "delete from pass_station_records where device_id = @DeviceId;", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, "delete from device_logs where device_id = @DeviceId;", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, "delete from hourly_capacity where device_id = @DeviceId;", deviceId, cancellationToken);
            await DeleteRowsAsync(connection, transaction, "delete from recipes where device_id = @DeviceId;", deviceId, cancellationToken);

            var deviceRows = await DeleteRowsAsync(
                connection,
                transaction,
                "delete from devices where id = @DeviceId;",
                deviceId,
                cancellationToken);

            transaction.Commit();
            return new DeviceCascadeDeletionResult(deviceRows > 0, impact);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<DeviceDeletionImpact> GetImpactAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                (select count(*) from recipes where device_id = @DeviceId) as Recipes,
                (select count(*) from hourly_capacity where device_id = @DeviceId) as Capacities,
                (select count(*) from device_logs where device_id = @DeviceId) as DeviceLogs,
                (select count(*) from pass_station_records where device_id = @DeviceId) as PassStations,
                (select count(*) from edge_device_client_version_snapshots where device_id = @DeviceId) as ClientVersionSnapshots,
                (
                    select count(*)
                    from edge_device_client_plugin_versions plugin
                    inner join edge_device_client_version_snapshots snapshot
                        on plugin.device_client_version_snapshot_id = snapshot.id
                    where snapshot.device_id = @DeviceId
                ) as ClientPluginVersions,
                (select count(*) from upload_receive_registrations where device_id = @DeviceId) as UploadReceiveRegistrations,
                (select count(*) from employee_device_accesses where device_id = @DeviceId) as EmployeeDeviceAccesses,
                (
                    select count(*)
                    from refresh_token_sessions
                    where "ActorType" = @ActorType
                      and "SubjectId" = @DeviceId
                ) as RefreshTokenSessions;
            """;

        var command = new CommandDefinition(
            sql,
            new { DeviceId = deviceId, ActorType = IIoTClaimTypes.EdgeDeviceActor },
            transaction,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<DeviceDeletionImpact>(command);
    }

    private static async Task<long> CountDeletedByReturningAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string sql,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var command = CreateDeleteCommand(transaction, sql, deviceId, cancellationToken);
        return await connection.ExecuteScalarAsync<long>(command);
    }

    private static async Task<long> DeleteRowsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string sql,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var command = CreateDeleteCommand(transaction, sql, deviceId, cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    private static CommandDefinition CreateDeleteCommand(
        IDbTransaction transaction,
        string sql,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            sql,
            new { DeviceId = deviceId, ActorType = IIoTClaimTypes.EdgeDeviceActor },
            transaction,
            cancellationToken: cancellationToken);
    }
}
