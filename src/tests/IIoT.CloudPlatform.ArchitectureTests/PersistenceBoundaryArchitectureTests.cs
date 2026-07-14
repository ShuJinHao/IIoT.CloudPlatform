using FluentValidation;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.EntityFrameworkCore.Uploads;
using IIoT.EventBus;
using IIoT.Infrastructure.Authentication;
using IIoT.IdentityService.Commands;
using IIoT.Infrastructure.Logging;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.SharedKernel.Configuration;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace IIoT.CloudPlatform.ArchitectureTests;

public sealed class PersistenceBoundaryArchitectureTests
{
    [Fact]
    public void DeviceCascadeDeletion_ShouldStayOnEfCoreWritePath()
    {
        var implementationSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "QueryServices",
            "EfDeviceDeletionDependencyService.cs"));
        var registrationSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "DependencyInjection.cs"));

        Assert.True(typeof(IDeviceDeletionDependencyQueryService).IsAssignableFrom(
            typeof(IIoT.EntityFrameworkCore.QueryServices.EfDeviceDeletionDependencyService)));
        Assert.DoesNotContain(
            typeof(IIoT.Dapper.DependencyInjection).Assembly.GetTypes(),
            type => !type.IsAbstract && typeof(IDeviceDeletionDependencyQueryService).IsAssignableFrom(type));
        Assert.Contains(
            "AddScoped<IDeviceDeletionDependencyQueryService, QueryServices.EfDeviceDeletionDependencyService>()",
            registrationSource,
            StringComparison.Ordinal);

        Assert.Contains("SqlQuery<DeviceDeletionImpactRow>", implementationSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteSqlInterpolatedAsync", implementationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CountTableAsync", implementationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteDeleteAsync", implementationSource, StringComparison.Ordinal);
        foreach (var table in new[]
                 {
                     "recipes", "hourly_capacity", "device_logs", "pass_station_records",
                     "edge_device_client_states", "edge_device_client_version_snapshots",
                     "edge_device_client_plugin_versions", "edge_device_runtime_heartbeats",
                     "upload_receive_registrations", "employee_device_accesses",
                     "refresh_token_sessions", "edge_host_plc_runtime_states"
                 })
        {
            Assert.Contains(table, implementationSource, StringComparison.Ordinal);
        }

        Assert.Contains("\"ActorType\"", implementationSource, StringComparison.Ordinal);
        Assert.Contains("\"SubjectId\"", implementationSource, StringComparison.Ordinal);
        var deleteSectionStart = implementationSource.IndexOf(
            "private async Task DeleteAssociatedRowsAsync",
            StringComparison.Ordinal);
        var deleteSectionEnd = implementationSource.IndexOf(
            "public sealed class DeviceDeletionImpactRow",
            StringComparison.Ordinal);
        Assert.True(deleteSectionStart >= 0 && deleteSectionEnd > deleteSectionStart);
        var deleteSection = implementationSource[deleteSectionStart..deleteSectionEnd];
        Assert.Contains("delete from edge_host_plc_runtime_states", deleteSection, StringComparison.Ordinal);
        Assert.DoesNotContain("delete from edge_hosts", deleteSection, StringComparison.Ordinal);
        Assert.DoesNotContain("delete from edge_host_plc_bindings", deleteSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshTokenSession_ShouldRemainInfrastructurePersistenceModel()
    {
        Assert.False(typeof(BaseEntity<Guid>).IsAssignableFrom(typeof(RefreshTokenSession)));
        Assert.False(typeof(IAggregateRoot).IsAssignableFrom(typeof(RefreshTokenSession)));
    }

    [Fact]
    public void BaseEntity_ShouldNotMakeEveryEntityAnAggregateRoot()
    {
        Assert.False(typeof(IAggregateRoot).IsAssignableFrom(typeof(BaseEntity<Guid>)));
        Assert.False(typeof(IAggregateRoot<Guid>).IsAssignableFrom(typeof(BaseEntity<Guid>)));

        Type[] aggregateRoots =
        [
            typeof(IdentityAccount),
            typeof(Employee),
            typeof(MfgProcess),
            typeof(Device),
            typeof(Recipe),
            typeof(ClientReleaseComponent),
            typeof(ClientReleaseRetentionPolicy)
        ];

        foreach (var aggregateRoot in aggregateRoots)
        {
            Assert.True(typeof(IAggregateRoot).IsAssignableFrom(aggregateRoot));
        }

        Type[] childEntities =
        [
            typeof(EmployeeDeviceAccess),
            typeof(ClientReleaseVersion),
            typeof(ClientReleaseArtifact),
            typeof(DeviceClientPluginVersion),
            typeof(DeviceClientVersionSnapshot),
            typeof(DeviceClientState),
            typeof(EdgeDeviceRuntimeHeartbeat),
            typeof(EdgeHostPlcRuntimeState)
        ];

        foreach (var childEntity in childEntities)
        {
            Assert.False(typeof(IAggregateRoot).IsAssignableFrom(childEntity));
        }
    }

    [Fact]
    public void DbContext_ShouldNotExposeReleaseChildEntitiesAsRootSets()
    {
        Assert.Null(typeof(IIoTDbContext).GetProperty("ClientReleaseVersions"));
        Assert.Null(typeof(IIoTDbContext).GetProperty("ClientReleaseArtifacts"));
        Assert.NotNull(typeof(IIoTDbContext).GetProperty(nameof(IIoTDbContext.ClientReleaseComponents)));
    }

    [Fact]
    public void DbContext_ShouldExposeOnlyEdgeHostRuntimeStateProjection()
    {
        Assert.Null(typeof(IIoTDbContext).GetProperty("EdgeHosts"));
        Assert.NotNull(typeof(IIoTDbContext).GetProperty(nameof(IIoTDbContext.EdgeHostPlcRuntimeStates)));
        Assert.Null(typeof(IIoTDbContext).GetProperty("EdgeHostPlcBindings"));
    }

    [Fact]
    public void IIoTDbContext_ShouldNotContainLegacyFlushDomainEventsPlaceholder()
    {
        Assert.Null(typeof(IIoTDbContext).GetMethod(
            "FlushDomainEventsAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic));
    }
}
