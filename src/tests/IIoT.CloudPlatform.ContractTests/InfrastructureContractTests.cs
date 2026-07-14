using IIoT.EntityFrameworkCore.Outbox;
using IIoT.Services.Contracts.Events.Capacities;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class InfrastructureContractTests
{
    [Fact]
    public void OutboxMessage_ShouldRoundTripIntegrationEvents()
    {
        var deviceId = Guid.NewGuid();
        var integrationEvent = new HourlyCapacityReceivedEvent
        {
            DeviceId = deviceId,
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

        var outboxMessage = OutboxMessage.FromIntegrationEvent(integrationEvent);
        var deserialized = Assert.IsType<HourlyCapacityReceivedEvent>(
            outboxMessage.DeserializeIntegrationEvent());

        Assert.Equal(OutboxMessageKind.IntegrationEvent, outboxMessage.MessageKind);
        Assert.Contains(nameof(HourlyCapacityReceivedEvent), outboxMessage.EventType);
        Assert.Equal(deviceId, deserialized.DeviceId);
        Assert.Equal(1, deserialized.SchemaVersion);
        Assert.Equal(integrationEvent.EventId, deserialized.EventId);
    }

}
