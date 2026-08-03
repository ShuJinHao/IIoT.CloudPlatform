using System.Text.Json;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class EfDeviceDeletionDependencyService(
    IIoTDbContext dbContext)
    : IDeviceDeletionDependencyQueryService
{
    public async Task<DeviceDeletionDependencies> GetDependenciesAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var impact = await GetImpactAsync(deviceId, cancellationToken);
        return new DeviceDeletionDependencies(
            impact.Recipes > 0,
            impact.Capacities > 0,
            impact.DeviceLogs > 0,
            impact.PassStations > 0);
    }

    public async Task<DeviceDeletionImpact> GetImpactAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var impact = await dbContext.Database.SqlQuery<DeviceDeletionImpactRow>($"""
            select
                (select count(*)::bigint from recipes where device_id = {deviceId}) as "Recipes",
                (select count(*)::bigint from hourly_capacity where device_id = {deviceId}) as "Capacities",
                (select count(*)::bigint from device_logs where device_id = {deviceId}) as "DeviceLogs",
                (select count(*)::bigint from pass_station_records where device_id = {deviceId}) as "PassStations",
                (select count(*)::bigint from edge_device_client_states where device_id = {deviceId}) as "ClientStates",
                (select count(*)::bigint from edge_device_client_version_snapshots where device_id = {deviceId}) as "ClientVersionSnapshots",
                (
                    select count(*)::bigint
                    from edge_device_client_plugin_versions plugin
                    where plugin.device_client_version_snapshot_id in (
                        select snapshot.id
                        from edge_device_client_version_snapshots snapshot
                        where snapshot.device_id = {deviceId}
                    )
                ) as "ClientPluginVersions",
                (select count(*)::bigint from edge_device_runtime_heartbeats where device_id = {deviceId}) as "RuntimeHeartbeats",
                (select count(*)::bigint from upload_receive_registrations where device_id = {deviceId}) as "UploadReceiveRegistrations",
                (select count(*)::bigint from employee_device_accesses where device_id = {deviceId}) as "EmployeeDeviceAccesses",
                (
                    select count(*)::bigint
                    from refresh_token_sessions
                    where "ActorType" = {IIoTClaimTypes.EdgeDeviceActor} and "SubjectId" = {deviceId}
                ) as "RefreshTokenSessions",
                (
                    select count(*)::bigint
                    from edge_host_plc_runtime_states state
                    where state.device_id = {deviceId}
                ) as "EdgeHostPlcRuntimeStates"
            """)
            .SingleAsync(cancellationToken);

        return impact.ToContract();
    }

    public async Task<DeviceProcessMigrationResult> MigrateProcessAsync(
        Guid deviceId,
        Guid expectedSourceProcessId,
        Guid targetProcessId,
        uint expectedRowVersion,
        DeviceProcessMigrationAuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        DeviceProcessMigrationResult? attemptedResult = null;
        uint? migratedRowVersion = null;
        var writeAttempted = false;
        var commitAttempted = false;
        var strategy = dbContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested
                  && !commitAttempted)
        {
            throw;
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch (Exception) when (commitAttempted)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.Devices
                .AsNoTracking()
                .Where(device => device.Id == deviceId)
                .Select(device => new
                {
                    device.ProcessId,
                    device.RowVersion
                })
                .SingleOrDefaultAsync(CancellationToken.None);
            if (current is not null
                && attemptedResult is not null
                && migratedRowVersion.HasValue
                && current.ProcessId == targetProcessId
                && current.RowVersion == migratedRowVersion.Value)
            {
                return attemptedResult;
            }

            throw new CloudWriteCommitUnknownException();
        }

        async Task<DeviceProcessMigrationResult> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            try
            {
                dbContext.ChangeTracker.Clear();
                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync(
                        transactionCancellationToken);
                await DeviceDeletionTransactionLock.AcquireAsync(
                    dbContext,
                    deviceId,
                    transactionCancellationToken);
                await LockProcessSemanticTablesAsync(
                    transactionCancellationToken);

                var lockedDevice = await LockDeviceProcessAsync(
                    deviceId,
                    transactionCancellationToken);
                if (lockedDevice is null)
                {
                    await transaction.RollbackAsync(
                        transactionCancellationToken);
                    return new DeviceProcessMigrationResult(
                        DeviceProcessMigrationStatus.DeviceNotFound,
                        deviceId,
                        null,
                        targetProcessId,
                        null,
                        EmptyImpact());
                }

                var targetExists = await LockTargetProcessAsync(
                    targetProcessId,
                    transactionCancellationToken);
                if (!targetExists)
                {
                    await transaction.RollbackAsync(
                        transactionCancellationToken);
                    return new DeviceProcessMigrationResult(
                        DeviceProcessMigrationStatus.TargetProcessNotFound,
                        deviceId,
                        lockedDevice.ProcessId,
                        targetProcessId,
                        lockedDevice.RowVersion,
                        EmptyImpact());
                }

                if (lockedDevice.ProcessId == targetProcessId)
                {
                    if (writeAttempted
                        && migratedRowVersion.HasValue
                        && lockedDevice.RowVersion == migratedRowVersion.Value
                        && attemptedResult is not null)
                    {
                        await transaction.RollbackAsync(
                            transactionCancellationToken);
                        return attemptedResult;
                    }

                    await transaction.RollbackAsync(
                        transactionCancellationToken);
                    return new DeviceProcessMigrationResult(
                        DeviceProcessMigrationStatus.SameProcess,
                        deviceId,
                        lockedDevice.ProcessId,
                        targetProcessId,
                        lockedDevice.RowVersion,
                        EmptyImpact());
                }

                if (lockedDevice.ProcessId != expectedSourceProcessId
                    || lockedDevice.RowVersion != expectedRowVersion)
                {
                    await transaction.RollbackAsync(
                        transactionCancellationToken);
                    throw new CloudWriteConflictException();
                }

                var impact = await GetImpactAsync(
                    deviceId,
                    transactionCancellationToken);
                if (HasProcessSemanticHistory(impact))
                {
                    await transaction.RollbackAsync(
                        transactionCancellationToken);
                    return new DeviceProcessMigrationResult(
                        DeviceProcessMigrationStatus.Blocked,
                        deviceId,
                        lockedDevice.ProcessId,
                        targetProcessId,
                        lockedDevice.RowVersion,
                        impact);
                }

                writeAttempted = true;
                var device = await dbContext.Devices.SingleAsync(
                    candidate => candidate.Id == deviceId,
                    transactionCancellationToken);
                if (device.RowVersion != expectedRowVersion
                    || device.ProcessId != expectedSourceProcessId)
                {
                    await transaction.RollbackAsync(
                        transactionCancellationToken);
                    throw new CloudWriteConflictException();
                }

                device.MigrateProcess(targetProcessId);
                var sourceProcess = await dbContext.MfgProcesses
                    .AsNoTracking()
                    .SingleAsync(
                        process => process.Id == expectedSourceProcessId,
                        transactionCancellationToken);
                var targetProcess = await dbContext.MfgProcesses
                    .AsNoTracking()
                    .SingleAsync(
                        process => process.Id == targetProcessId,
                        transactionCancellationToken);
                dbContext.AuditTrails.Add(AuditTrailRecord.FromEntry(
                    CreateMigrationAuditEntry(
                        auditContext,
                        device,
                        sourceProcess.ProcessCode,
                        targetProcess.ProcessCode,
                        expectedSourceProcessId,
                        targetProcessId,
                        expectedRowVersion,
                        impact)));
                await dbContext.SaveChangesAsync(transactionCancellationToken);
                migratedRowVersion = device.RowVersion;
                attemptedResult = new DeviceProcessMigrationResult(
                    DeviceProcessMigrationStatus.Migrated,
                    deviceId,
                    expectedSourceProcessId,
                    targetProcessId,
                    migratedRowVersion,
                    impact);
                commitAttempted = true;
                await transaction.CommitAsync(transactionCancellationToken);
                return attemptedResult;
            }
            catch
            {
                dbContext.DiscardPendingDomainEvents();
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }
    }

    public async Task<DeviceCascadeDeletionResult> DeleteCascadeAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        DeviceDeletionImpact? lastDeletionAttemptImpact = null;
        DeviceDeletionImpact? lastCommitAttemptImpact = null;
        DeviceDeletionImpact? committedReplayCleanupImpact = null;
        DeviceDeletionImpact? pendingReplayCleanupImpact = null;
        string? pendingReplayCleanupTransactionId = null;
        var deletionAttempted = false;
        var commitAttempted = false;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (Exception exception)
            when (commitAttempted
                  && lastCommitAttemptImpact is not null)
        {
            throw new DeviceDeletionCommitAttemptException(
                exception,
                lastCommitAttemptImpact);
        }

        async Task<DeviceCascadeDeletionResult> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            try
            {
                dbContext.ChangeTracker.Clear();
                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync(
                        transactionCancellationToken);
                await DeviceDeletionTransactionLock.AcquireAsync(
                    dbContext,
                    deviceId,
                    transactionCancellationToken);
                await ResolvePendingReplayCleanupAsync(
                    transactionCancellationToken);
                var lockedDeviceRowVersion = await LockDeviceAsync(
                    deviceId,
                    transactionCancellationToken);
                if (!lockedDeviceRowVersion.HasValue)
                {
                    var remainingImpact = await GetImpactAsync(
                        deviceId,
                        transactionCancellationToken);

                    if (!deletionAttempted)
                    {
                        return new DeviceCascadeDeletionResult(
                            false,
                            remainingImpact);
                    }

                    if (remainingImpact.TotalAssociatedRows > 0)
                    {
                        var cleanupAttemptImpact = remainingImpact;
                        await DeleteAssociatedRowsAsync(
                            deviceId,
                            transactionCancellationToken);
                        remainingImpact = await GetImpactAsync(
                            deviceId,
                            transactionCancellationToken);
                        if (remainingImpact.TotalAssociatedRows == 0)
                        {
                            var cleanupTransactionId =
                                await GetCurrentTransactionIdAsync(
                                    transactionCancellationToken);
                            pendingReplayCleanupTransactionId =
                                cleanupTransactionId;
                            pendingReplayCleanupImpact =
                                cleanupAttemptImpact;
                        }
                    }

                    var committedReplayConfirmed =
                        remainingImpact.TotalAssociatedRows == 0;
                    var committedImpact = AddImpacts(
                        AddImpacts(
                            lastDeletionAttemptImpact
                            ?? remainingImpact,
                            committedReplayCleanupImpact),
                        pendingReplayCleanupImpact);
                    if (committedReplayConfirmed)
                    {
                        lastCommitAttemptImpact = committedImpact;
                        commitAttempted = true;
                        await transaction.CommitAsync(
                            transactionCancellationToken);
                        CommitPendingReplayCleanup();
                    }

                    return new DeviceCascadeDeletionResult(
                        committedReplayConfirmed,
                        committedReplayConfirmed
                            ? committedImpact
                            : remainingImpact);
                }

                if (expectedRowVersion.HasValue
                    && lockedDeviceRowVersion.Value
                    != expectedRowVersion.Value)
                {
                    throw new CloudWriteConflictException();
                }

                var impact = await GetImpactAsync(
                    deviceId,
                    transactionCancellationToken);
                lastDeletionAttemptImpact = impact;
                deletionAttempted = true;
                await DeleteAssociatedRowsAsync(
                    deviceId,
                    transactionCancellationToken);

                var device = await dbContext.Devices.SingleAsync(
                    candidate => candidate.Id == deviceId,
                    transactionCancellationToken);
                device.MarkDeleted();
                dbContext.Devices.Remove(device);

                var affectedRows = await dbContext.SaveChangesAsync(
                    transactionCancellationToken);
                lastCommitAttemptImpact = impact;
                commitAttempted = true;
                await transaction.CommitAsync(transactionCancellationToken);
                return new DeviceCascadeDeletionResult(
                    affectedRows > 0,
                    impact);
            }
            catch
            {
                dbContext.DiscardPendingDomainEvents();
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        async Task ResolvePendingReplayCleanupAsync(
            CancellationToken transactionCancellationToken)
        {
            if (pendingReplayCleanupImpact is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(
                    pendingReplayCleanupTransactionId))
            {
                throw new InvalidOperationException(
                    "Device deletion replay cleanup commit state cannot be verified.");
            }

            var transactionStatus = await dbContext.Database
                .SqlQuery<string>($"""
                    SELECT pg_xact_status(
                        CAST({pendingReplayCleanupTransactionId} AS xid8)
                    ) AS "Value"
                    """)
                .SingleAsync(transactionCancellationToken);
            if (string.Equals(
                    transactionStatus,
                    "committed",
                    StringComparison.Ordinal))
            {
                committedReplayCleanupImpact =
                    committedReplayCleanupImpact is null
                        ? pendingReplayCleanupImpact
                        : AddImpacts(
                            committedReplayCleanupImpact,
                            pendingReplayCleanupImpact);
            }
            else if (!string.Equals(
                         transactionStatus,
                         "aborted",
                         StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Device deletion replay cleanup transaction is unexpectedly '{transactionStatus}'.");
            }

            pendingReplayCleanupImpact = null;
            pendingReplayCleanupTransactionId = null;
        }

        void CommitPendingReplayCleanup()
        {
            if (pendingReplayCleanupImpact is null)
            {
                return;
            }

            committedReplayCleanupImpact =
                committedReplayCleanupImpact is null
                    ? pendingReplayCleanupImpact
                    : AddImpacts(
                        committedReplayCleanupImpact,
                        pendingReplayCleanupImpact);
            pendingReplayCleanupImpact = null;
            pendingReplayCleanupTransactionId = null;
        }
    }

    private async Task<string?> GetCurrentTransactionIdAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return null;
        }

        return await dbContext.Database
            .SqlQuery<string>($"""
                SELECT pg_current_xact_id()::text AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    private static DeviceDeletionImpact AddImpacts(
        DeviceDeletionImpact impact,
        DeviceDeletionImpact? additionalImpact)
    {
        if (additionalImpact is null)
        {
            return impact;
        }

        return new DeviceDeletionImpact(
            impact.Recipes + additionalImpact.Recipes,
            impact.Capacities + additionalImpact.Capacities,
            impact.DeviceLogs + additionalImpact.DeviceLogs,
            impact.PassStations + additionalImpact.PassStations,
            impact.ClientStates + additionalImpact.ClientStates,
            impact.ClientVersionSnapshots + additionalImpact.ClientVersionSnapshots,
            impact.ClientPluginVersions + additionalImpact.ClientPluginVersions,
            impact.UploadReceiveRegistrations + additionalImpact.UploadReceiveRegistrations,
            impact.EmployeeDeviceAccesses + additionalImpact.EmployeeDeviceAccesses,
            impact.RefreshTokenSessions + additionalImpact.RefreshTokenSessions,
            impact.RuntimeHeartbeats + additionalImpact.RuntimeHeartbeats,
            impact.EdgeHostPlcRuntimeStates + additionalImpact.EdgeHostPlcRuntimeStates);
    }

    private async Task<uint?> LockDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var lockedDeviceRowVersion = await dbContext.Database
            .SqlQuery<string>($"""
                SELECT xmin::text AS "Value"
                FROM devices
                WHERE id = {deviceId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        return uint.TryParse(
            lockedDeviceRowVersion,
            out var parsedRowVersion)
            ? parsedRowVersion
            : null;
    }

    private async Task<LockedDeviceProcessRow?> LockDeviceProcessAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Database
            .SqlQuery<LockedDeviceProcessRow>($"""
                SELECT
                    process_id AS "ProcessId",
                    xmin::text AS "RowVersionText"
                FROM devices
                WHERE id = {deviceId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> LockTargetProcessAsync(
        Guid targetProcessId,
        CancellationToken cancellationToken)
    {
        var lockedTarget = await dbContext.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM mfg_processes
                WHERE id = {targetProcessId}
                FOR KEY SHARE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        return lockedTarget != Guid.Empty;
    }

    private async Task LockProcessSemanticTablesAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            "LOCK TABLE recipes, hourly_capacity, pass_station_records, "
            + "edge_host_plc_runtime_states IN SHARE ROW EXCLUSIVE MODE;",
            cancellationToken);
    }

    private static bool HasProcessSemanticHistory(DeviceDeletionImpact impact)
        => impact.Recipes > 0
           || impact.Capacities > 0
           || impact.PassStations > 0
           || impact.EdgeHostPlcRuntimeStates > 0;

    private static DeviceDeletionImpact EmptyImpact()
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static AuditTrailEntry CreateMigrationAuditEntry(
        DeviceProcessMigrationAuditContext context,
        Device device,
        string sourceProcessCode,
        string targetProcessCode,
        Guid sourceProcessId,
        Guid targetProcessId,
        uint expectedRowVersion,
        DeviceDeletionImpact impact)
    {
        var executedAtUtc = context.ExecutedAtUtc.Kind == DateTimeKind.Utc
            ? context.ExecutedAtUtc
            : context.ExecutedAtUtc.ToUniversalTime();
        executedAtUtc = new DateTime(
            executedAtUtc.Ticks - executedAtUtc.Ticks % 10,
            DateTimeKind.Utc);
        var summary = JsonSerializer.Serialize(new
        {
            action = "DeviceProcessMigration",
            deviceId = device.Id,
            clientCode = device.Code,
            sourceProcessId,
            sourceProcessCode,
            targetProcessId,
            targetProcessCode,
            expectedRowVersion,
            counts = new
            {
                recipes = impact.Recipes,
                capacities = impact.Capacities,
                passStations = impact.PassStations,
                plcRuntimeStates = impact.EdgeHostPlcRuntimeStates
            },
            preserved = "DeviceId,ClientCode,BootstrapSecret,Access,Version,Heartbeat"
        });
        if (summary.Length > 512)
        {
            throw new InvalidOperationException(
                "Device process migration audit summary exceeds 512 characters.");
        }

        return new AuditTrailEntry(
            context.ActorUserId,
            context.ActorEmployeeNo,
            "Device.Process.Migrate",
            "Device",
            device.Id.ToString(),
            executedAtUtc,
            true,
            summary,
            IdempotencyKey:
                $"device-process-migrate:{device.Id:N}:"
                + $"{expectedRowVersion}:{targetProcessId:N}");
    }

    private async Task DeleteAssociatedRowsAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            delete from edge_host_plc_runtime_states
            where device_id = {deviceId};

            delete from edge_device_client_plugin_versions
            where device_client_version_snapshot_id in (
                select id
                from edge_device_client_version_snapshots
                where device_id = {deviceId}
            );

            delete from edge_device_client_states
            where device_id = {deviceId};

            delete from edge_device_client_version_snapshots
            where device_id = {deviceId};

            delete from edge_device_runtime_heartbeats
            where device_id = {deviceId};

            delete from upload_receive_registrations
            where device_id = {deviceId};

            delete from employee_device_accesses
            where device_id = {deviceId};

            delete from refresh_token_sessions
            where "ActorType" = {IIoTClaimTypes.EdgeDeviceActor} and "SubjectId" = {deviceId};

            delete from pass_station_records
            where device_id = {deviceId};

            delete from device_logs
            where device_id = {deviceId};

            delete from hourly_capacity
            where device_id = {deviceId};

            delete from recipes
            where device_id = {deviceId};
            """, cancellationToken);
    }

    public sealed class DeviceDeletionImpactRow
    {
        public long Recipes { get; set; }

        public long Capacities { get; set; }

        public long DeviceLogs { get; set; }

        public long PassStations { get; set; }

        public long ClientStates { get; set; }

        public long ClientVersionSnapshots { get; set; }

        public long ClientPluginVersions { get; set; }

        public long RuntimeHeartbeats { get; set; }

        public long UploadReceiveRegistrations { get; set; }

        public long EmployeeDeviceAccesses { get; set; }

        public long RefreshTokenSessions { get; set; }

        public long EdgeHostPlcRuntimeStates { get; set; }

        public DeviceDeletionImpact ToContract()
        {
            return new DeviceDeletionImpact(
                Recipes,
                Capacities,
                DeviceLogs,
                PassStations,
                ClientStates,
                ClientVersionSnapshots,
                ClientPluginVersions,
                UploadReceiveRegistrations,
                EmployeeDeviceAccesses,
                RefreshTokenSessions,
                RuntimeHeartbeats,
                EdgeHostPlcRuntimeStates);
        }
    }

    public sealed class LockedDeviceProcessRow
    {
        public Guid ProcessId { get; set; }

        public string RowVersionText { get; set; } = string.Empty;

        public uint RowVersion => uint.TryParse(
            RowVersionText,
            out var parsedRowVersion)
            ? parsedRowVersion
            : throw new InvalidOperationException(
                "Device xmin row version is invalid.");
    }
}
