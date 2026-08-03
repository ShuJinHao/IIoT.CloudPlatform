using IIoT.CloudPlatform.TestKit;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Paging;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class DeviceProcessMigrationBehaviorTests
{
    [Fact]
    public void MigrationRequests_ShouldRequireAdminOnlyDedicatedPermissionAndAuditMetadata()
    {
        var deviceId = Guid.NewGuid();
        var query = new GetDeviceProcessMigrationImpactQuery(
            deviceId,
            Guid.NewGuid());
        var command = new MigrateDeviceProcessCommand(
            deviceId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            7,
            "confirm");

        AssertDedicatedPermission(typeof(GetDeviceProcessMigrationImpactQuery));
        AssertDedicatedPermission(typeof(MigrateDeviceProcessCommand));
        Assert.Equal(
            "Device.ProcessMigrationImpact.Read",
            ((IAdminOnlyAuditRequest)query).AdminAuditOperationType);
        Assert.Equal(
            "Device.Process.Migrate",
            ((IAdminOnlyAuditRequest)command).AdminAuditOperationType);
        Assert.Contains(DevicePermissions.MigrateProcess, CloudPermissionCatalog.All);
        Assert.DoesNotContain(
            DevicePermissions.MigrateProcess,
            CloudPermissionCatalog.RoleAdminAssignable);
        foreach (var permissions in SystemRolePermissionTemplates.Templates.Values)
        {
            Assert.DoesNotContain(DevicePermissions.MigrateProcess, permissions);
        }
    }

    [Fact]
    public async Task Impact_ShouldListProcessSemanticBlockersButKeepIdentityStateNonBlocking()
    {
        var sourceProcessId = Guid.NewGuid();
        var targetProcessId = Guid.NewGuid();
        var device = new Device(
            "Negative electrode client",
            "DEV-NEGATIVE01",
            sourceProcessId);
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var processQueries = new StubProcessReadQueryService();
        processQueries.PagedProcesses.AddRange(
        [
            new ProcessReadItem(sourceProcessId, "CP", "模切"),
            new ProcessReadItem(targetProcessId, "AP", "负极模切")
        ]);
        var dependencies = new StubDeviceDeletionDependencyQueryService
        {
            Impact = new DeviceDeletionImpact(
                Recipes: 2,
                Capacities: 3,
                DeviceLogs: 5,
                PassStations: 4,
                ClientStates: 1,
                ClientVersionSnapshots: 1,
                ClientPluginVersions: 2,
                UploadReceiveRegistrations: 6,
                EmployeeDeviceAccesses: 7,
                RefreshTokenSessions: 8,
                RuntimeHeartbeats: 9,
                EdgeHostPlcRuntimeStates: 10)
        };
        var handler = new GetDeviceProcessMigrationImpactHandler(
            repository,
            processQueries,
            dependencies,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true });

        var result = await handler.Handle(
            new GetDeviceProcessMigrationImpactQuery(device.Id, targetProcessId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var impact = Assert.IsType<DeviceProcessMigrationImpactDto>(result.Value);
        Assert.False(impact.CanMigrate);
        Assert.Equal(
            [
                "recipes",
                "hourly_capacity",
                "pass_station_records",
                "edge_host_plc_runtime_states"
            ],
            impact.Blockers.Select(blocker => blocker.Code));
        Assert.DoesNotContain(
            impact.Blockers,
            blocker => blocker.Code is "device_logs"
                or "edge_device_client_states"
                or "edge_device_runtime_heartbeats");
        Assert.Equal("MIGRATE DEV-NEGATIVE01 TO AP", impact.ConfirmationText);
        Assert.Equal(device.RowVersion, impact.RowVersion);
    }

    [Fact]
    public async Task Command_ShouldBindPreflightStateAndTransactionalAuditIdentity()
    {
        var sourceProcessId = Guid.NewGuid();
        var targetProcessId = Guid.NewGuid();
        var device = new Device(
            "Negative electrode client",
            "DEV-NEGATIVE02",
            sourceProcessId);
        device.SetBootstrapSecretHash("hash-that-must-not-change");
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var processQueries = new StubProcessReadQueryService();
        processQueries.PagedProcesses.AddRange(
        [
            new ProcessReadItem(sourceProcessId, "CP", "模切"),
            new ProcessReadItem(targetProcessId, "AP", "负极模切")
        ]);
        var migration = new StubDeviceDeletionDependencyQueryService();
        var audit = new RecordingAuditTrailService();
        var handler = new MigrateDeviceProcessHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin",
                ActorType = IIoTClaimTypes.HumanActor,
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            repository,
            processQueries,
            migration,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            audit);
        var command = new MigrateDeviceProcessCommand(
            device.Id,
            sourceProcessId,
            targetProcessId,
            device.RowVersion,
            "MIGRATE DEV-NEGATIVE02 TO AP");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(device.Id, migration.LastMigrationDeviceId);
        Assert.Equal(sourceProcessId, migration.LastExpectedSourceProcessId);
        Assert.Equal(targetProcessId, migration.LastTargetProcessId);
        Assert.Equal(device.RowVersion, migration.LastExpectedRowVersion);
        Assert.NotNull(migration.LastMigrationAuditContext);
        Assert.Equal("admin", migration.LastMigrationAuditContext!.ActorEmployeeNo);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task LedgerProcessOptions_ShouldExposeEmptyProcessesOnlyToAdmin()
    {
        var occupiedProcessId = Guid.NewGuid();
        var emptyProcessId = Guid.NewGuid();
        var device = new Device(
            "Scoped device",
            "DEV-SCOPED001",
            occupiedProcessId);
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(device);
        var processQueries = new StubProcessReadQueryService();
        processQueries.PagedProcesses.AddRange(
        [
            new ProcessReadItem(occupiedProcessId, "CP", "模切"),
            new ProcessReadItem(emptyProcessId, "AP", "负极模切")
        ]);

        var adminResult = await new GetDeviceLedgerProcessOptionsHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            processQueries).Handle(
                new GetDeviceLedgerProcessOptionsQuery(),
                CancellationToken.None);
        var operatorResult = await new GetDeviceLedgerProcessOptionsHandler(
            new StubCurrentUserDeviceAccessService
            {
                AccessibleDeviceIds = [device.Id]
            },
            repository,
            processQueries).Handle(
                new GetDeviceLedgerProcessOptionsQuery(),
                CancellationToken.None);

        Assert.True(adminResult.IsSuccess);
        Assert.Equal(2, adminResult.Value!.Count);
        Assert.True(operatorResult.IsSuccess);
        Assert.Single(operatorResult.Value!);
        Assert.Equal(occupiedProcessId, operatorResult.Value![0].Id);
    }

    [Fact]
    public async Task DeviceLedgerQuery_ShouldApplySelectedProcessAsAndFilter()
    {
        var selectedProcessId = Guid.NewGuid();
        var otherProcessId = Guid.NewGuid();
        var selected = new Device(
            "Selected process device",
            "DEV-PROCESS01",
            selectedProcessId);
        var other = new Device(
            "Other process device",
            "DEV-PROCESS02",
            otherProcessId);
        selected.ClearDomainEvents();
        other.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.AddRange([selected, other]);
        var handler = new GetMyDevicesPagedHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository);

        var result = await handler.Handle(
            new GetMyDevicesPagedQuery(
                new Pagination { PageNumber = 1, PageSize = 10 },
                ProcessId: selectedProcessId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(selected.Id, item.Id);
        Assert.Equal(selectedProcessId, item.ProcessId);
    }

    [Fact]
    public void DeviceMigration_ShouldChangeOnlyProcessAndRaiseExactDomainEvent()
    {
        var sourceProcessId = Guid.NewGuid();
        var targetProcessId = Guid.NewGuid();
        var device = new Device(
            "Stable identity",
            "DEV-STABLE001",
            sourceProcessId);
        device.SetBootstrapSecretHash("stable-hash");
        device.ClearDomainEvents();

        device.MigrateProcess(targetProcessId);

        Assert.Equal(targetProcessId, device.ProcessId);
        Assert.Equal("Stable identity", device.DeviceName);
        Assert.Equal("DEV-STABLE001", device.Code);
        Assert.Equal("stable-hash", device.BootstrapSecretHash);
        var domainEvent = Assert.Single(device.DomainEvents);
        Assert.Equal(
            "DeviceProcessMigratedDomainEvent",
            domainEvent.GetType().Name);
    }

    private static void AssertDedicatedPermission(Type requestType)
    {
        var permission = requestType
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), false)
            .Cast<AuthorizeRequirementAttribute>()
            .Single();
        Assert.Equal(DevicePermissions.MigrateProcess, permission.Permission);
        Assert.Single(requestType.GetCustomAttributes(typeof(AdminOnlyAttribute), false));
    }
}
