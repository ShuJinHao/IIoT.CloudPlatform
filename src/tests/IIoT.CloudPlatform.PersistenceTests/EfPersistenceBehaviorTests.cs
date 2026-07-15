using System.IO;
using FluentValidation;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Contracts.EdgeHosts;
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

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EfPersistenceBehaviorTests
{
    [Fact]
    public void AggregateConfigurations_ShouldMarkRowVersionAsConcurrencyToken()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();

        Assert.True(dbContext.Model.FindEntityType(typeof(Employee))!
            .FindProperty(nameof(Employee.RowVersion))!
            .IsConcurrencyToken);
        Assert.True(dbContext.Model.FindEntityType(typeof(Device))!
            .FindProperty(nameof(Device.RowVersion))!
            .IsConcurrencyToken);
        Assert.True(dbContext.Model.FindEntityType(typeof(Recipe))!
            .FindProperty(nameof(Recipe.RowVersion))!
            .IsConcurrencyToken);
        Assert.True(dbContext.Model.FindEntityType(typeof(MfgProcess))!
            .FindProperty(nameof(MfgProcess.RowVersion))!
            .IsConcurrencyToken);
        Assert.True(dbContext.Model.FindEntityType(typeof(RefreshTokenSession))!
            .FindProperty(nameof(RefreshTokenSession.RowVersion))!
            .IsConcurrencyToken);
    }

    [Fact]
    public async Task EfRepository_Update_ShouldPreserveTrackedEntityChangedProperties()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var repository = new EfRepository<Device>(dbContext);
        var device = new Device("Device-01", "DEV-TRACKED001", Guid.NewGuid());

        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();

        device.Rename("Device-02");
        dbContext.ChangeTracker.DetectChanges();

        repository.Update(device);

        var modifiedProperties = dbContext.Entry(device)
            .Properties
            .Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(Device.DeviceName), modifiedProperties);
        Assert.DoesNotContain(nameof(Device.Code), modifiedProperties);
        Assert.DoesNotContain(nameof(Device.ProcessId), modifiedProperties);
    }

    [Fact]
    public async Task EfRepository_Update_ShouldRejectDetachedAggregateToAvoidFullEntityUpdate()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var repository = new EfRepository<Device>(dbContext);
        var device = new Device("Device-01", "DEV-DETACHED001", Guid.NewGuid());

        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(device).State = EntityState.Detached;

        device.Rename("Device-02");

        var exception = Assert.Throws<InvalidOperationException>(() => repository.Update(device));
        Assert.Contains("Detached aggregate updates are not supported", exception.Message);
    }

    [Fact]
    public void EdgeHostPlcRuntimeState_ShouldUseDedicatedStore()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ConnectionResourceNames.IiotDatabase}"] =
                "Host=127.0.0.1;Port=5432;Database=registration_only;Username=test;Password=test",
            [$"{PostgresOptions.SectionName}:EnableRetry"] = "false",
            [$"{PostgresOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{PostgresOptions.SectionName}:MaxRetryCount"] = "0",
            [$"{PostgresOptions.SectionName}:MaxRetryDelaySeconds"] = "1"
        });
        builder.AddEfCore();

        Assert.True(typeof(IEdgeHostPlcRuntimeStateStore).IsAssignableFrom(
            typeof(IIoT.EntityFrameworkCore.EdgeHosts.EfEdgeHostPlcRuntimeStateStore)));
        var registration = Assert.Single(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IEdgeHostPlcRuntimeStateStore));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(
            typeof(IIoT.EntityFrameworkCore.EdgeHosts.EfEdgeHostPlcRuntimeStateStore),
            registration.ImplementationType);
    }

    [Fact]
    public async Task DbContext_SaveChanges_ShouldPersistDomainEventsToOutbox()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(dbContext, "E1001", "Operator");
        await dbContext.SaveChangesAsync();

        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Contains(nameof(EmployeeOnboardedDomainEvent), outboxMessage.EventType);
        Assert.Contains("E1001", outboxMessage.Payload, StringComparison.Ordinal);
        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.Empty(employee.DomainEvents);
    }

    [Fact]
    public async Task IntegrationEventOutbox_ShouldPersistIntegrationEventMessages()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var outbox = new EfIntegrationEventOutbox(dbContext);
        var integrationEvent = new HourlyCapacityReceivedEvent
        {
            DeviceId = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftCode = "D",
            Hour = 10,
            Minute = 0,
            TimeLabel = "10:00",
            TotalCount = 20,
            OkCount = 19,
            NgCount = 1,
            ReceivedAtUtc = DateTime.UtcNow
        };

        await outbox.EnqueueAsync(integrationEvent);

        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageKind.IntegrationEvent, outboxMessage.MessageKind);
        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.Equal(integrationEvent.EventId, Assert.IsType<HourlyCapacityReceivedEvent>(
            outboxMessage.DeserializeIntegrationEvent()).EventId);
    }

    [Fact]
    public void RecipeConfiguration_ShouldAddStandaloneDeviceIdIndex()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var recipeEntityType = dbContext.Model.FindEntityType(typeof(Recipe));
        var hasStandaloneDeviceIdIndex = recipeEntityType!.GetIndexes()
            .Any(index =>
                index.Properties.Count == 1
                && string.Equals(index.Properties[0].Name, nameof(Recipe.DeviceId), StringComparison.Ordinal));

        Assert.True(hasStandaloneDeviceIdIndex);
    }

    [Fact]
    public void AuditTrailConfiguration_ShouldAddActorUserIdIndex()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var auditTrailEntityType = dbContext.Model.FindEntityType(typeof(AuditTrailRecord));
        var hasActorUserIdIndex = auditTrailEntityType!.GetIndexes()
            .Any(index =>
                index.Properties.Count == 1
                && string.Equals(index.Properties[0].Name, nameof(AuditTrailRecord.ActorUserId), StringComparison.Ordinal));

        Assert.True(hasActorUserIdIndex);
    }

    [Fact]
    public void OutboxConfiguration_ShouldMapMessageKindDefault()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var outboxEntityType = dbContext.Model.FindEntityType(typeof(OutboxMessage));
        var messageKindProperty = outboxEntityType!.FindProperty(nameof(OutboxMessage.MessageKind));

        Assert.NotNull(messageKindProperty);
        Assert.Equal("message_kind", messageKindProperty!.GetColumnName());
        Assert.Equal(
            nameof(OutboxMessageKind.DomainEvent),
            messageKindProperty.GetDefaultValue()?.ToString());
    }

    [Fact]
    public void OutboxConfiguration_ShouldMapAbandonedStateAndIndexes()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var outboxEntityType = dbContext.Model.FindEntityType(typeof(OutboxMessage));
        var abandonedProperty = outboxEntityType!.FindProperty(nameof(OutboxMessage.AbandonedAtUtc));
        var indexNames = outboxEntityType.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotNull(abandonedProperty);
        Assert.Equal("abandoned_at_utc", abandonedProperty!.GetColumnName());
        Assert.Contains("ix_outbox_messages_abandoned", indexNames);
        Assert.Contains("ix_outbox_messages_dispatch", indexNames);
    }

    [Fact]
    public void UploadReceiveRegistrationConfiguration_ShouldAddDeduplicationUniqueIndex()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var entityType = dbContext.Model.FindEntityType(typeof(UploadReceiveRegistration));
        var index = Assert.Single(
            entityType!.GetIndexes(),
            candidate => string.Equals(
                candidate.GetDatabaseName(),
                "ux_upload_receive_registrations_device_message_deduplication",
                StringComparison.Ordinal));

        Assert.Equal("upload_receive_registrations", entityType.GetTableName());
        Assert.True(index.IsUnique);
        Assert.Equal(
            [
                nameof(UploadReceiveRegistration.DeviceId),
                nameof(UploadReceiveRegistration.MessageType),
                nameof(UploadReceiveRegistration.DeduplicationKey)
            ],
            index.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task UploadReceiveRegistry_ShouldRegisterOnceAndReturnDuplicateForSameDeduplicationKey()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var registry = new EfUploadReceiveRegistry(dbContext);
        var deviceId = Guid.NewGuid();
        var firstEvent = new HourlyCapacityReceivedEvent
        {
            DeviceId = deviceId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftCode = "D",
            Hour = 9,
            Minute = 30,
            TimeLabel = "09:30",
            TotalCount = 16,
            OkCount = 15,
            NgCount = 1,
            ReceivedAtUtc = DateTime.UtcNow
        };
        var duplicateEvent = firstEvent with
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow.AddSeconds(1)
        };

        var first = await registry.RegisterAndEnqueueAsync(
            deviceId,
            "hourly-capacity",
            " request-1 ",
            "request:request-1",
            firstEvent);
        var second = await registry.RegisterAndEnqueueAsync(
            deviceId,
            "hourly-capacity",
            "request-1",
            "request:request-1",
            duplicateEvent);

        var registration = await dbContext.UploadReceiveRegistrations.SingleAsync();
        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.OutboxMessageId, second.OutboxMessageId);
        Assert.Single(dbContext.OutboxMessages);
        Assert.Equal("request-1", registration.RequestId);
        Assert.Equal(2, registration.SeenCount);
        Assert.True(registration.LastSeenAtUtc >= registration.ReceivedAtUtc);
    }

    private static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.Create(_ => { }).CreateLogger<T>();
    }
}
