using System.Reflection;
using System.Text.Json;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class ProductionContractShapeTests
{
    [Fact]
    public void EdgeInstallerArtifactManifest_ShouldExposeVelopackSetupFileContract()
    {
        var manifestType = typeof(GenerateEdgeInstallerPackageHandler).Assembly.GetType(
            "IIoT.ProductionService.Commands.ClientReleases.EdgeInstallerArtifactManifest");

        Assert.NotNull(manifestType);
        Assert.NotNull(manifestType!.GetProperty("VelopackSetupFile"));
    }

    [Fact]
    public void EventContracts_ShouldDefaultMissingSchemaVersionToV1()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Equal(1, new HourlyCapacityReceivedEvent().SchemaVersion);
        Assert.Equal(1, new DeviceLogReceivedEvent().SchemaVersion);
        Assert.Equal(1, new PassStationBatchReceivedEvent().SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<HourlyCapacityReceivedEvent>("{}", options)!.SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<DeviceLogReceivedEvent>("{}", options)!.SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<PassStationBatchReceivedEvent>("{}", options)!.SchemaVersion);
    }

    [Fact]
    public void EventContracts_ShouldExposeIntegrationEventBoundary()
    {
        var eventTypes = new[]
        {
            typeof(HourlyCapacityReceivedEvent),
            typeof(DeviceLogReceivedEvent),
            typeof(PassStationBatchReceivedEvent)
        };

        foreach (var eventType in eventTypes)
        {
            Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType), eventType.FullName);
        }

        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(typeof(IPassStationEvent)));

        var publishMethod = typeof(IEventPublisher)
            .GetMethods()
            .Single(method =>
                string.Equals(method.Name, nameof(IEventPublisher.PublishAsync), StringComparison.Ordinal)
                && method.IsGenericMethodDefinition);
        var genericParameter = Assert.Single(publishMethod.GetGenericArguments());
        Assert.Contains(
            typeof(IIntegrationEvent),
            genericParameter.GetGenericParameterConstraints());

        IIntegrationEvent[] events =
        [
            new HourlyCapacityReceivedEvent(),
            new DeviceLogReceivedEvent(),
            new PassStationBatchReceivedEvent()
        ];

        foreach (var @event in events)
        {
            Assert.NotEqual(Guid.Empty, @event.EventId);
            Assert.True(@event.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(1, @event.SchemaVersion);
        }
    }
}
