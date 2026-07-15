using MassTransit;

namespace IIoT.EntityFrameworkCore.Outbox;

public static class IntegrationEventInboxDefaults
{
    public static readonly TimeSpan DuplicateDetectionWindow = TimeSpan.FromDays(7);

    public static readonly TimeSpan QueryDelay = TimeSpan.FromSeconds(1);

    public static void ConfigurePostgres(IEntityFrameworkOutboxConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        configurator.UsePostgres();
        configurator.DuplicateDetectionWindow = DuplicateDetectionWindow;
        configurator.QueryDelay = QueryDelay;
    }
}
