using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.ClientReleases;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class ClientReleaseWriteRetryPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallerGenerationStore_ShouldRecoverWriteFaultAndKeepRecordImmutable(
        bool throwAfterCommit)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var fault = new ArmableSaveChangesFault(throwAfterCommit);
        var options = CreateWriteOptions(budget.ConnectionString, fault);
        var store = new EfEdgeInstallerGenerationStore(
            options,
            NullLogger<EfEdgeInstallerGenerationStore>.Instance);
        var generationId = Guid.NewGuid();
        var record = new EdgeInstallerGenerationRecord(
            generationId,
            Guid.NewGuid(),
            "installer-operator",
            new DateTime(2026, 8, 2, 1, 30, 0, DateTimeKind.Utc),
            "stable",
            "win-x64",
            "1.2.0",
            new string('a', 64),
            "IIoT.EdgeClient-installer.exe",
            new string('b', 64),
            1024,
            [new EdgeInstallerGenerationBindingFact(
                "CP",
                Guid.NewGuid(),
                "DEV-CP01",
                "正极模切客户端",
                Guid.NewGuid())],
            [new EdgeInstallerGenerationPluginFact(
                "CP",
                "2.3.4",
                new string('c', 64))]);

        try
        {
            fault.Arm();
            Assert.True(await store.TryAddConfirmedAsync(record, budget.Token));
            Assert.Equal(1, fault.ExceptionsThrown);
            Assert.True(await store.TryAddConfirmedAsync(record, budget.Token));

            var persisted = await store.GetByIdAsync(generationId, budget.Token);
            Assert.NotNull(persisted);
            Assert.Equal(record.PackageSha256, persisted.PackageSha256);
            Assert.True(JsonElement.DeepEquals(
                JsonDocument.Parse(record.BindingsJson).RootElement,
                JsonDocument.Parse(persisted.BindingsJson).RootElement));
            Assert.True(JsonElement.DeepEquals(
                JsonDocument.Parse(record.PluginsJson).RootElement,
                JsonDocument.Parse(persisted.PluginsJson).RootElement));

            var conflicting = new EdgeInstallerGenerationRecord(
                generationId,
                record.OperatorUserId,
                record.OperatorName,
                record.GeneratedAtUtc,
                record.Channel,
                record.TargetRuntime,
                record.HostVersion,
                record.HostSha256,
                record.FileName,
                new string('d', 64),
                record.PackageSize,
                [new EdgeInstallerGenerationBindingFact(
                    "CP",
                    Guid.NewGuid(),
                    "DEV-CP01",
                    "正极模切客户端",
                    Guid.NewGuid())],
                [new EdgeInstallerGenerationPluginFact(
                    "CP",
                    "2.3.4",
                    new string('c', 64))]);
            Assert.False(await store.TryAddConfirmedAsync(conflicting, budget.Token));
            Assert.Equal(
                record.PackageSha256,
                (await store.GetByIdAsync(generationId, budget.Token))!.PackageSha256);
        }
        finally
        {
            await using var cleanup = new IIoTDbContext(
                CreateObservationOptions(budget.ConnectionString));
            await cleanup.EdgeInstallerGenerationRecords
                .Where(candidate => candidate.Id == generationId)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task InstallerPendingActivationLifecycle_ShouldPersistReadyConfirmAndExpiry()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var options = CreateObservationOptions(budget.ConnectionString);
        var store = new EfEdgeInstallerGenerationStore(
            options,
            NullLogger<EfEdgeInstallerGenerationStore>.Instance);
        var process = new MfgProcess(
            $"IPL-{Guid.NewGuid():N}"[..20],
            "Installer pending lifecycle");
        var device = new Device(
            $"Installer lifecycle {Guid.NewGuid():N}"[..40],
            $"IPL-{Guid.NewGuid():N}"[..20],
            process.Id);
        device.SetBootstrapSecretHash(
            BootstrapSecretHasher.Hash(BootstrapSecretGenerator.Generate()));
        var generationIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        try
        {
            await using (var setup = new IIoTDbContext(options))
            {
                setup.MfgProcesses.Add(process);
                setup.Devices.Add(device);
                await setup.SaveChangesAsync(budget.Token);
            }

            foreach (var generationId in generationIds)
            {
                var record = new EdgeInstallerGenerationRecord(
                    generationId,
                    null,
                    "installer-lifecycle-test",
                    DateTime.UtcNow,
                    "stable",
                    "win-x64",
                    "2.0.12",
                    new string('a', 64),
                    $"installer-{generationId:N}.exe",
                    new string('b', 64),
                    1024,
                    [new EdgeInstallerGenerationBindingFact(
                        "LIFECYCLE",
                        device.Id,
                        device.Code,
                        device.DeviceName,
                        process.Id)],
                    [new EdgeInstallerGenerationPluginFact(
                        "LIFECYCLE",
                        "2.0.12",
                        new string('c', 64))]);
                var pending = new EdgeInstallerPendingCredential(
                    generationId,
                    device.Id,
                    device.Code,
                    BootstrapSecretHasher.Hash(BootstrapSecretGenerator.Generate()),
                    "LIFECYCLE",
                    "2.0.12",
                    new string('b', 64),
                    DateTime.UtcNow.AddDays(7));
                Assert.True(await store.TryAddConfirmedAsync(
                    record,
                    [pending],
                    budget.Token));
            }

            var readyAtUtc = new DateTime(
                DateTime.UtcNow.Ticks - DateTime.UtcNow.Ticks % 10,
                DateTimeKind.Utc);
            Assert.Equal(
                EdgeInstallerActivationAttempt.Started,
                await store.TryActivateAsync(
                    generationIds[0],
                    device.Id,
                    4312,
                    readyAtUtc,
                    budget.Token));
            Assert.Equal(
                EdgeInstallerActivationAttempt.Confirmed,
                await store.ConfirmActivationAsync(
                    generationIds[0],
                    device.Id,
                    4312,
                    readyAtUtc,
                    budget.Token));

            await using (var expireSetup = new IIoTDbContext(options))
            {
                await expireSetup.EdgeInstallerPendingCredentials
                    .Where(item => item.GenerationId == generationIds[1]
                                   || item.GenerationId == generationIds[2])
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.ExpiresAtUtc,
                            DateTime.UtcNow.AddMinutes(-1)),
                        budget.Token);
            }

            var directlyExpired = await store.GetPendingAsync(
                generationIds[1],
                device.Id,
                budget.Token);
            Assert.NotNull(directlyExpired);
            Assert.Equal(
                EdgeInstallerPendingCredentialStatus.Expired,
                directlyExpired.Status);
            Assert.False(await store.HasDownloadablePendingAsync(
                generationIds[2],
                DateTime.UtcNow,
                budget.Token));

            await using var observation = new IIoTDbContext(options);
            var persisted = await observation.EdgeInstallerPendingCredentials
                .AsNoTracking()
                .Where(item => generationIds.Contains(item.GenerationId))
                .OrderBy(item => item.GenerationId)
                .ToListAsync(budget.Token);
            Assert.Equal(3, persisted.Count);
            Assert.Contains(
                persisted,
                item => item.GenerationId == generationIds[0]
                        && item.Status == EdgeInstallerPendingCredentialStatus.Activated
                        && item.ReadyProcessId == 4312);
            Assert.Equal(
                2,
                persisted.Count(item =>
                    item.Status == EdgeInstallerPendingCredentialStatus.Expired));
            Assert.Null(await observation.Devices
                .AsNoTracking()
                .Where(item => item.Id == device.Id)
                .Select(item => item.BootstrapSecretHash)
                .SingleAsync(budget.Token));
        }
        finally
        {
            await using var cleanup = new IIoTDbContext(options);
            await cleanup.EdgeInstallerPendingCredentials
                .Where(item => generationIds.Contains(item.GenerationId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.EdgeInstallerGenerationRecords
                .Where(item => generationIds.Contains(item.Id))
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.Devices
                .Where(item => item.Id == device.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.MfgProcesses
                .Where(item => item.Id == process.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProductionBindingApplyStore_ShouldPersistBindingAndAuditAtomically()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var options = CreateObservationOptions(budget.ConnectionString);
        var process = new MfgProcess(
            $"PBA-{Guid.NewGuid():N}"[..20],
            "Production binding apply");
        var device = new Device(
            $"Binding apply {Guid.NewGuid():N}"[..40],
            $"PBA-{Guid.NewGuid():N}"[..20],
            process.Id);
        var component = CreatePluginComponent(
            $"PBA-{Guid.NewGuid():N}"[..24],
            "stable",
            "2.0.12",
            "plugins/pba/2.0.12/plugin.zip",
            new string('d', 64),
            1024);
        component.ConfigurePluginContract(
            process.ProcessCode,
            "docs/plugins/pba.md",
            2,
            new string('e', 64),
            "[]");
        var bindingId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var request = new ProductionBindingApplyRequest(
            bindingId,
            device.Id,
            component.Id,
            process.ProcessCode,
            DateTime.UtcNow,
            auditId,
            new AuditTrailEntry(
                Guid.NewGuid(),
                "binding-admin",
                "DevicePluginBinding.ProductionApply",
                "Device",
                device.Id.ToString("D"),
                DateTime.UtcNow,
                true,
                "PostgreSQL binding apply evidence.",
                IdempotencyKey: $"binding-apply:{bindingId:N}"));

        try
        {
            await using (var setup = new IIoTDbContext(options))
            {
                setup.MfgProcesses.Add(process);
                setup.Devices.Add(device);
                setup.ClientReleaseComponents.Add(component);
                await setup.SaveChangesAsync(budget.Token);
            }

            var store = new EfProductionBindingApplyStore(options);
            Assert.Equal(
                ProductionBindingApplyResult.Applied,
                await store.TryApplyAsync(request, budget.Token));
            Assert.Equal(
                ProductionBindingApplyResult.Idempotent,
                await store.TryApplyAsync(request, budget.Token));

            await using var observation = new IIoTDbContext(options);
            var binding = await observation.DevicePluginBindings
                .AsNoTracking()
                .SingleAsync(item => item.Id == bindingId, budget.Token);
            Assert.Equal(device.Id, binding.DeviceId);
            Assert.Equal(component.Id, binding.ClientReleaseComponentId);
            Assert.True(await observation.ClientReleaseComponents
                .AsNoTracking()
                .Where(item => item.Id == component.Id)
                .Select(item => item.WasEverDeviceBound)
                .SingleAsync(budget.Token));
            Assert.True(await observation.AuditTrails
                .AsNoTracking()
                .AnyAsync(item => item.Id == auditId, budget.Token));
        }
        finally
        {
            await using var cleanup = new IIoTDbContext(options);
            await cleanup.DevicePluginBindings
                .Where(item => item.Id == bindingId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.AuditTrails
                .Where(item => item.Id == auditId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.ClientReleaseComponents
                .Where(item => item.Id == component.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.Devices
                .Where(item => item.Id == device.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.MfgProcesses
                .Where(item => item.Id == process.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EdgeRefreshReplacement_ShouldRevokePriorSessionAndPersistReplacement()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var options = CreateObservationOptions(budget.ConnectionString);
        var process = new MfgProcess(
            $"ERR-{Guid.NewGuid():N}"[..20],
            "Edge refresh replacement");
        var device = new Device(
            $"Refresh replacement {Guid.NewGuid():N}"[..40],
            $"ERR-{Guid.NewGuid():N}"[..20],
            process.Id);

        try
        {
            await using (var setup = new IIoTDbContext(options))
            {
                setup.MfgProcesses.Add(process);
                setup.Devices.Add(device);
                await setup.SaveChangesAsync(budget.Token);
            }

            await using var serviceContext = new IIoTDbContext(options);
            var service = new EfRefreshTokenService(
                serviceContext,
                Options.Create(new RefreshTokenOptions()));
            await service.IssueAsync(
                IIoTClaimTypes.EdgeDeviceActor,
                device.Id,
                budget.Token);
            var replacement = await service.IssueReplacingAsync(
                IIoTClaimTypes.EdgeDeviceActor,
                device.Id,
                "installer-v3-activated",
                budget.Token);

            await using var observation = new IIoTDbContext(options);
            var sessions = await observation.RefreshTokenSessions
                .AsNoTracking()
                .Where(item => item.ActorType == IIoTClaimTypes.EdgeDeviceActor
                               && item.SubjectId == device.Id)
                .ToListAsync(budget.Token);
            Assert.Equal(2, sessions.Count);
            var prior = Assert.Single(
                sessions,
                item => item.RevokedReason == "installer-v3-activated");
            var active = Assert.Single(sessions, item => item.RevokedAtUtc is null);
            Assert.Equal(active.Id, prior.ReplacedByTokenId);
            Assert.Equal(
                replacement.ExpiresAtUtc,
                active.ExpiresAtUtc);
        }
        finally
        {
            await using var cleanup = new IIoTDbContext(options);
            await cleanup.RefreshTokenSessions
                .Where(item => item.ActorType == IIoTClaimTypes.EdgeDeviceActor
                               && item.SubjectId == device.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.Devices
                .Where(item => item.Id == device.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.MfgProcesses
                .Where(item => item.Id == process.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss(
        bool throwAfterCommit)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(120));
        var fault = new ArmableSaveChangesFault(throwAfterCommit);
        var writeOptions = CreateWriteOptions(
            budget.ConnectionString,
            fault);
        var observationOptions = CreateObservationOptions(
            budget.ConnectionString);
        var edgeRoot = Path.Combine(
            Path.GetTempPath(),
            $"iiot-client-release-pg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(edgeRoot);

        try
        {
            var observationReader =
                new CloudWriteObservationReader(observationOptions);
            var auditTrail = new EfAuditTrailService(
                new IIoTDbContext(observationOptions),
                observationOptions,
                NullLogger<EfAuditTrailService>.Instance);
            var currentUser = CreateAdmin();

            await using (var dbContext =
                         new IIoTDbContext(writeOptions))
            {
                await VerifyArchiveAsync(
                    dbContext,
                    new EfRepository<ClientReleaseComponent>(
                        dbContext),
                    observationReader,
                    fault,
                    budget.Token);
            }

            await using (var dbContext =
                         new IIoTDbContext(writeOptions))
            {
                await VerifyPolicyAndRetentionAsync(
                    dbContext,
                    new EfRepository<ClientReleaseComponent>(
                        dbContext),
                    new EfRepository<
                        ClientReleaseRetentionPolicy>(dbContext),
                    new EfDeviceClientStateStore(dbContext),
                    observationReader,
                    fault,
                    budget.Token);
            }

            await using (var dbContext =
                         new IIoTDbContext(writeOptions))
            {
                await VerifyPackageDeletionAsync(
                    dbContext,
                    new EfRepository<ClientReleaseComponent>(
                        dbContext),
                    new EfDeviceClientStateStore(dbContext),
                    observationReader,
                    auditTrail,
                    currentUser,
                    fault,
                    edgeRoot,
                    budget.Token);
            }

            await using (var dbContext =
                         new IIoTDbContext(writeOptions))
            {
                await VerifyHardDeletionAsync(
                    dbContext,
                    new EfRepository<ClientReleaseComponent>(
                        dbContext),
                    new EfDeviceClientStateStore(dbContext),
                    observationReader,
                    auditTrail,
                    currentUser,
                    fault,
                    edgeRoot,
                    budget.Token);
            }

            await using (var dbContext =
                         new IIoTDbContext(writeOptions))
            {
                await VerifyInstallerSecretRotationAsync(
                    dbContext,
                    new EfRepository<ClientReleaseComponent>(
                        dbContext),
                    new EfRepository<Device>(dbContext),
                    observationReader,
                    auditTrail,
                    currentUser,
                    fault,
                    edgeRoot,
                    budget.Token);
            }

            Assert.Equal(6, fault.ExceptionsThrown);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    private static async Task VerifyArchiveAsync(
        IIoTDbContext dbContext,
        EfRepository<ClientReleaseComponent> componentRepository,
        CloudWriteObservationReader observationReader,
        ArmableSaveChangesFault fault,
        CancellationToken cancellationToken)
    {
        var channel = UniqueSegment("pg-archive");
        var component = CreateHostComponent(
            channel,
            "1.0.0",
            "win-x64",
            ClientReleaseStatus.Published);
        var versionId = Assert.Single(component.Versions).Id;
        dbContext.ClientReleaseComponents.Add(component);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var expectedFaults = fault.ExceptionsThrown + 1;
        fault.Arm();
        var result = await new ArchiveClientReleaseHandler(
                componentRepository,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new ArchiveClientReleaseCommand(versionId),
                cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedFaults, fault.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            ClientReleaseStatus.Archived,
            await dbContext.Set<ClientReleaseVersion>()
                .AsNoTracking()
                .Where(version => version.Id == versionId)
                .Select(version => version.Status)
                .SingleAsync(cancellationToken));
    }

    private static async Task VerifyPolicyAndRetentionAsync(
        IIoTDbContext dbContext,
        EfRepository<ClientReleaseComponent> componentRepository,
        EfRepository<ClientReleaseRetentionPolicy> policyRepository,
        EfDeviceClientStateStore clientStateStore,
        CloudWriteObservationReader observationReader,
        ArmableSaveChangesFault fault,
        CancellationToken cancellationToken)
    {
        var currentMax = await dbContext.ClientReleaseRetentionPolicies
            .AsNoTracking()
            .Where(policy =>
                policy.Id
                == ClientReleaseRetentionPolicy.SingletonId)
            .Select(policy => (int?)policy.MaxVersionsPerComponent)
            .SingleOrDefaultAsync(cancellationToken);
        var targetMax = currentMax == 2 ? 3 : 2;
        var expectedFaults = fault.ExceptionsThrown + 1;
        fault.Arm();
        var policyResult =
            await new UpdateClientReleaseRetentionPolicyHandler(
                    policyRepository,
                    componentRepository,
                    new NoopRetentionService(),
                    CreateUnitOfWork(dbContext),
                    observationReader)
                .Handle(
                    new UpdateClientReleaseRetentionPolicyCommand(
                        targetMax),
                    cancellationToken);
        Assert.True(policyResult.IsSuccess);
        Assert.Equal(expectedFaults, fault.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            targetMax,
            await dbContext.ClientReleaseRetentionPolicies
                .AsNoTracking()
                .Where(policy =>
                    policy.Id
                    == ClientReleaseRetentionPolicy.SingletonId)
                .Select(policy => policy.MaxVersionsPerComponent)
                .SingleAsync(cancellationToken));

        var channel = UniqueSegment("pg-retention");
        var component = ClientReleaseComponent.CreateHost(
            channel,
            "win-x64");
        for (var index = 0; index <= targetMax; index++)
        {
            var version = $"1.0.{index}";
            component.UpsertHostVersion(
                version,
                "1.0.0",
                "net10.0",
                $"/edge-updates/installers/{channel}/{version}/installer-artifact.json",
                new string((char)('a' + index), 64),
                1024 + index,
                $"retention {index}",
                ClientReleaseStatus.Published,
                null,
                "IIoT");
        }

        dbContext.ClientReleaseComponents.Add(component);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var retentionService = new ClientReleaseRetentionService(
            policyRepository,
            componentRepository,
            clientStateStore,
            Options.Create(new EdgeReleaseRetentionOptions()),
            CreateUnitOfWork(dbContext),
            observationReader);
        expectedFaults = fault.ExceptionsThrown + 1;
        fault.Arm();
        await retentionService.ApplyHostPolicyAsync(
            channel,
            "win-x64",
            cancellationToken);
        Assert.Equal(expectedFaults, fault.ExceptionsThrown);

        dbContext.ChangeTracker.Clear();
        var statuses = await dbContext.Set<ClientReleaseVersion>()
            .AsNoTracking()
            .Where(version =>
                version.ClientReleaseComponentId == component.Id)
            .Select(version => version.Status)
            .ToListAsync(cancellationToken);
        Assert.Equal(
            targetMax,
            statuses.Count(
                status => status == ClientReleaseStatus.Published));
        Assert.Single(
            statuses,
            status => status == ClientReleaseStatus.Archived);
    }

    private static async Task VerifyPackageDeletionAsync(
        IIoTDbContext dbContext,
        EfRepository<ClientReleaseComponent> componentRepository,
        EfDeviceClientStateStore clientStateStore,
        CloudWriteObservationReader observationReader,
        EfAuditTrailService auditTrail,
        ICurrentUser currentUser,
        ArmableSaveChangesFault fault,
        string edgeRoot,
        CancellationToken cancellationToken)
    {
        var channel = UniqueSegment("pg-package");
        const string versionText = "2.0.0";
        var manifestPath = Path.Combine(
            edgeRoot,
            "installers",
            channel,
            versionText,
            "installer-artifact.json");
        WriteFile(manifestPath, "{}");
        var component = CreateHostComponent(
            channel,
            versionText,
            "win-x64",
            ClientReleaseStatus.Published,
            FileSha256(manifestPath),
            new FileInfo(manifestPath).Length);
        var versionId = Assert.Single(component.Versions).Id;
        dbContext.ClientReleaseComponents.Add(component);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var expectedFaults = fault.ExceptionsThrown + 1;
        fault.Arm();
        var result = await new DeleteClientReleasePackageHandler(
                Options.Create(new EdgeInstallerArtifactOptions
                {
                    RootPath = Path.Combine(
                        edgeRoot,
                        "installers")
                }),
                componentRepository,
                clientStateStore,
                currentUser,
                auditTrail,
                NullLogger<
                    DeleteClientReleasePackageHandler>.Instance,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new DeleteClientReleasePackageCommand(
                    versionId,
                    "postgres retry verification"),
                cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedFaults, fault.ExceptionsThrown);
        Assert.False(Directory.Exists(
            Path.GetDirectoryName(manifestPath)!));
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            ClientReleaseStatus.Deleted,
            await dbContext.Set<ClientReleaseVersion>()
                .AsNoTracking()
                .Where(version => version.Id == versionId)
                .Select(version => version.Status)
                .SingleAsync(cancellationToken));
        var auditKey =
            $"client-release-package-delete:{versionId:N}";
        Assert.Equal(
            1,
            await dbContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.IdempotencyKey == auditKey,
                    cancellationToken));
    }

    private static async Task VerifyHardDeletionAsync(
        IIoTDbContext dbContext,
        EfRepository<ClientReleaseComponent> componentRepository,
        EfDeviceClientStateStore clientStateStore,
        CloudWriteObservationReader observationReader,
        EfAuditTrailService auditTrail,
        ICurrentUser currentUser,
        ArmableSaveChangesFault fault,
        string edgeRoot,
        CancellationToken cancellationToken)
    {
        var channel = UniqueSegment("pg-hard");
        const string moduleId = "PGHardDelete";
        const string versionText = "3.0.0";
        var packageRelativePath =
            $"plugins/{channel}/{moduleId}/{versionText}/{moduleId}.zip";
        var packagePath = Path.Combine(
            edgeRoot,
            packageRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        WriteFile(packagePath, "postgres hard delete package");
        var component = CreatePluginComponent(
            moduleId,
            channel,
            versionText,
            packageRelativePath,
            FileSha256(packagePath),
            new FileInfo(packagePath).Length);
        Assert.Single(component.Versions)
            .ChangeStatus(ClientReleaseStatus.Archived);
        var componentId = component.Id;
        dbContext.ClientReleaseComponents.Add(component);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var deletionStore =
            new EfClientReleaseComponentDeletionStore(dbContext);
        var unitOfWork = CreateUnitOfWork(dbContext);
        var processor =
            new ClientReleaseComponentDeletionProcessor(
                Options.Create(new EdgeInstallerArtifactOptions
                {
                    RootPath = Path.Combine(
                        edgeRoot,
                        "installers")
                }),
                componentRepository,
                deletionStore,
                auditTrail,
                NullLogger<
                    ClientReleaseComponentDeletionProcessor>.Instance,
                unitOfWork,
                observationReader);
        var expectedFaults = fault.ExceptionsThrown + 1;
        fault.Arm();
        var result =
            await new HardDeleteClientReleaseComponentHandler(
                Options.Create(
                    new EdgeInstallerArtifactOptions
                    {
                        RootPath = Path.Combine(
                            edgeRoot,
                            "installers")
                    }),
                componentRepository,
                clientStateStore,
                new StubDevicePluginBindingQueryService(),
                deletionStore,
                processor,
                currentUser,
                auditTrail,
                unitOfWork,
                observationReader)
            .Handle(
                new HardDeleteClientReleaseComponentCommand(
                    componentId,
                    "postgres retry verification",
                    $"DELETE {moduleId}"),
                cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedFaults, fault.ExceptionsThrown);
        Assert.False(File.Exists(packagePath));
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.ClientReleaseComponents
            .AsNoTracking()
            .AnyAsync(
                current => current.Id == componentId,
                cancellationToken));
        Assert.False(await dbContext.ClientReleaseComponentDeletions
            .AsNoTracking()
            .AnyAsync(
                deletion =>
                    deletion.ComponentId == componentId,
                cancellationToken));
        var auditKey =
            $"client-release-hard-delete-completed:{result.Value!.DeletionId:N}";
        Assert.Equal(
            1,
            await dbContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.IdempotencyKey == auditKey
                        && record.Succeeded,
                    cancellationToken));
        Assert.Equal(
            0,
            await dbContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.TargetIdOrKey
                        == result.Value.DeletionId.ToString()
                        && !record.Succeeded,
                    cancellationToken));
    }

    private static async Task VerifyInstallerSecretRotationAsync(
        IIoTDbContext dbContext,
        EfRepository<ClientReleaseComponent> componentRepository,
        EfRepository<Device> deviceRepository,
        CloudWriteObservationReader observationReader,
        EfAuditTrailService auditTrail,
        ICurrentUser currentUser,
        ArmableSaveChangesFault fault,
        string edgeRoot,
        CancellationToken cancellationToken)
    {
        const string channel = "stable";
        const string runtime = "win-x64";
        const string hostVersion = "4.0.0";
        const string pluginVersion = "4.1.0";
        const string moduleId = "PGInstaller";
        var installerRoot = Path.Combine(edgeRoot, "installers");
        CreateInstallerArtifact(
            installerRoot,
            channel,
            hostVersion,
            runtime);
        var pluginRelativePath =
            $"plugins/{channel}/{moduleId}/{pluginVersion}/{moduleId}.zip";
        var pluginPath = Path.Combine(
            edgeRoot,
            pluginRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        CreatePluginPackage(
            pluginPath,
            moduleId,
            pluginVersion);

        var host = CreateHostComponent(
            channel,
            hostVersion,
            runtime,
            ClientReleaseStatus.Published);
        var plugin = CreatePluginComponent(
            moduleId,
            channel,
            pluginVersion,
            pluginRelativePath,
            FileSha256(pluginPath),
            new FileInfo(pluginPath).Length);
        var unique = Guid.NewGuid().ToString("N");
        var process = new MfgProcess(
            $"PGI-{unique}"[..20],
            "Postgres installer process");
        plugin.ConfigurePluginContract(
            process.ProcessCode,
            "docs/postgres-installer-plugin.md",
            2,
            new string('a', 64),
            "[]");
        var device = new Device(
            $"Postgres installer {unique}"[..40],
            $"PGI-{unique}"[..20],
            process.Id);
        var oldHash = BootstrapSecretHasher.Hash(
            BootstrapSecretGenerator.Generate());
        device.SetBootstrapSecretHash(oldHash);
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        dbContext.ClientReleaseComponents.AddRange(host, plugin);
        var binding = new DevicePluginBinding(
            device.Id,
            plugin.Id,
            process.ProcessCode,
            DateTime.UtcNow);
        plugin.MarkDeviceBound();
        dbContext.DevicePluginBindings.Add(binding);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var access = new StubCurrentUserDeviceAccessService
        {
            IsAdministrator = true
        };
        var bindingQuery = new StubDevicePluginBindingQueryService
        {
            Bindings =
            [
                new DevicePluginBindingReadItem(
                    binding.Id,
                    device.Id,
                    plugin.Id,
                    plugin.ComponentKey,
                    plugin.DisplayName,
                    process.ProcessCode,
                    channel,
                    runtime,
                    plugin.DataCapabilitiesJson)
            ]
        };
        var planService = new EdgeInstallerPlanService(
            access,
            deviceRepository,
            componentRepository,
            bindingQuery);
        var planResult = await planService.BuildAsync(
            [device.Id],
            cancellationToken);
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;

        var expectedFaults = fault.ExceptionsThrown + 1;
        fault.Arm();
        var result =
            await new GenerateEdgeInstallerPackageHandler(
                    currentUser,
                    access,
                    deviceRepository,
                    componentRepository,
                    auditTrail,
                    Options.Create(
                        new EdgeInstallerArtifactOptions
                        {
                            RootPath = installerRoot
                    }),
                    observationReader,
                    new InMemoryEdgeInstallerGenerationStore(),
                    planService)
                .Handle(
                    new GenerateEdgeInstallerPackageCommand(
                        BaseUrl: "http://cloud.local",
                        DeviceIds: [device.Id],
                        PlanFingerprint: plan.PlanFingerprint),
                    cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedFaults, fault.ExceptionsThrown);
        await result.Value!.Content.DisposeAsync();
        dbContext.ChangeTracker.Clear();
        var targetHash = await dbContext.Devices
            .AsNoTracking()
            .Where(current => current.Id == device.Id)
            .Select(current => current.BootstrapSecretHash)
            .SingleAsync(cancellationToken);
        Assert.NotNull(targetHash);
        Assert.NotEqual(oldHash, targetHash);
        Assert.Equal(
            1,
            await dbContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    record =>
                        record.OperationType
                        == "Edge.GenerateInstallerPackage"
                        && record.TargetIdOrKey
                        == device.Id.ToString()
                        && record.Succeeded,
                    cancellationToken));
    }

    private static DbContextOptions<IIoTDbContext>
        CreateWriteOptions(
            string connectionString,
            ArmableSaveChangesFault fault)
        => new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromMilliseconds(50),
                    null))
            .AddInterceptors(fault)
            .Options;

    private static DbContextOptions<IIoTDbContext>
        CreateObservationOptions(string connectionString)
        => new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromMilliseconds(50),
                    null))
            .Options;

    private static EfUnitOfWork CreateUnitOfWork(
        IIoTDbContext dbContext)
        => new(
            dbContext,
            NullLogger<EfUnitOfWork>.Instance);

    private static ICurrentUser CreateAdmin()
        => new TestCurrentUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "pg-client-release-admin",
            Roles = [SystemRoles.Admin],
            ActorType = IIoTClaimTypes.HumanActor,
            IsAuthenticated = true
        };

    private static ClientReleaseComponent CreateHostComponent(
        string channel,
        string version,
        string targetRuntime,
        ClientReleaseStatus status,
        string? manifestSha256 = null,
        long? manifestSize = null)
    {
        var component = ClientReleaseComponent.CreateHost(
            channel,
            targetRuntime);
        component.UpsertHostVersion(
            version,
            "1.0.0",
            "net10.0",
            $"/edge-updates/installers/{channel}/{version}/installer-artifact.json",
            manifestSha256 ?? new string('a', 64),
            manifestSize ?? 1024,
            "postgres client release",
            status,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.InstallerDirectory,
                    $"installers/{channel}/{version}"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.ManifestFile,
                    $"installers/{channel}/{version}/installer-artifact.json",
                    manifestSha256 ?? new string('a', 64),
                    manifestSize ?? 1024)
            ]);
        return component;
    }

    private static ClientReleaseComponent CreatePluginComponent(
        string moduleId,
        string channel,
        string version,
        string packageRelativePath,
        string sha256,
        long packageSize)
    {
        var component = ClientReleaseComponent.CreatePlugin(
            moduleId,
            moduleId,
            null,
            null,
            null,
            channel,
            "win-x64");
        component.UpsertPluginVersion(
            version,
            "1.0.0",
            "1.0.0",
            "99.0.0",
            "net10.0",
            $"/edge-updates/{packageRelativePath}",
            sha256,
            packageSize,
            "postgres client release",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind
                        .PluginPackageDirectory,
                    Path.GetDirectoryName(packageRelativePath)!
                        .Replace('\\', '/')),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    packageRelativePath,
                    sha256,
                    packageSize)
            ]);
        return component;
    }

    private static void CreateInstallerArtifact(
        string installerRoot,
        string channel,
        string version,
        string targetRuntime)
    {
        var artifactRoot = Path.Combine(
            installerRoot,
            channel,
            version);
        WriteFile(
            Path.Combine(
                artifactRoot,
                "IIoT.Edge.Setup.exe"),
            "MZ-postgres-installer");
        WriteFile(
            Path.Combine(
                artifactRoot,
                "launcher",
                "IIoT.Edge.Launcher.dll"),
            "launcher");
        WriteFile(
            Path.Combine(
                artifactRoot,
                "host",
                "IIoT.Edge.Shell.dll"),
            "host");
        Directory.CreateDirectory(
            Path.Combine(artifactRoot, "plugins"));
        WriteFile(
            Path.Combine(
                artifactRoot,
                "velopack",
                "IIoT.EdgeClient-Setup.exe"),
            "velopack setup");
        File.WriteAllText(
            Path.Combine(
                artifactRoot,
                "installer-artifact.json"),
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 2,
                    installerBindingSchemaVersion = 2,
                    channel,
                    version,
                    hostApiVersion = "1.0.0",
                    targetRuntime,
                    targetFramework = "net10.0",
                    installerStubFile = "IIoT.Edge.Setup.exe",
                    launcherDirectory = "launcher",
                    hostDirectory = "host",
                    pluginsRoot = "plugins",
                    velopackSetupFile =
                        "velopack/IIoT.EdgeClient-Setup.exe",
                    modules = Array.Empty<object>()
                }),
            new UTF8Encoding(false));
    }

    private static void CreatePluginPackage(
        string packagePath,
        string moduleId,
        string version)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(
            packagePath,
            ZipArchiveMode.Create);
        var manifest = archive.CreateEntry("plugin.json");
        using (var writer = new StreamWriter(
                   manifest.Open(),
                   new UTF8Encoding(false)))
        {
            writer.Write(JsonSerializer.Serialize(new
            {
                moduleId,
                version,
                hostApiVersion = "1.0.0",
                minHostVersion = "1.0.0",
                maxHostVersion = "99.0.0",
                entryAssembly =
                    $"IIoT.Edge.Module.{moduleId}.dll"
            }));
        }

        var assembly = archive.CreateEntry(
            $"IIoT.Edge.Module.{moduleId}.dll");
        using var assemblyWriter = new StreamWriter(
            assembly.Open(),
            new UTF8Encoding(false));
        assemblyWriter.Write("postgres plugin");
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(false));
    }

    private static string FileSha256(string path)
        => Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string UniqueSegment(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}"[..24];

    private sealed class NoopRetentionService
        : IClientReleaseRetentionService
    {
        public Task ApplyHostPolicyAsync(
            string channel,
            string targetRuntime,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ApplyPluginPolicyAsync(
            string moduleId,
            string channel,
            string targetRuntime,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> GetMaxVersionsPerComponentAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(3);
    }

    private sealed class ArmableSaveChangesFault(
        bool throwAfterCommit) : SaveChangesInterceptor
    {
        private int armed;
        private int exceptionsThrown;

        public int ExceptionsThrown =>
            Volatile.Read(ref exceptionsThrown);

        public void Arm() => Volatile.Write(ref armed, 1);

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (!throwAfterCommit
                && Interlocked.CompareExchange(
                    ref armed,
                    0,
                    1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient before commit");
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
                SaveChangesCompletedEventData eventData,
                int result,
                CancellationToken cancellationToken = default)
        {
            if (throwAfterCommit)
            {
                if (Interlocked.CompareExchange(
                    ref armed,
                    0,
                    1) == 1)
                {
                    Interlocked.Increment(ref exceptionsThrown);
                    throw RetryablePostgresException(
                        "simulated commit confirmation loss");
                }
            }

            return ValueTask.FromResult(result);
        }
    }

    private static PostgresException RetryablePostgresException(
        string message)
        => new(
            message,
            "ERROR",
            "ERROR",
            PostgresErrorCodes.SerializationFailure);
}
