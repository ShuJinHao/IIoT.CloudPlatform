using IIoT.DataWorker.Consumers;
using IIoT.DataWorker.Outbox;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.Gateway.Infrastructure;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class GatewayAndEventContractTests
{
    [Fact]
    public void AppHostTestingGuard_RequiresAnAlreadyTrustedTestingEnvironment()
    {
        AppHostTestingGuard.EnsureAllowed(
            disableDataWorkerOutboxDispatcher: false,
            Environments.Production);
        AppHostTestingGuard.EnsureAllowed(
            disableDataWorkerOutboxDispatcher: true,
            "Testing");

        var error = Assert.Throws<InvalidOperationException>(() =>
            AppHostTestingGuard.EnsureAllowed(
                disableDataWorkerOutboxDispatcher: true,
                Environments.Production));
        Assert.Contains("restricted to the Testing environment", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutboxDispatcherRegistration_TestOnlyDisableKeepsProductionFailClosed()
    {
        static IConfiguration Configuration(bool disabled) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataWorker:Testing:DisableOutboxDispatcher"] = disabled.ToString()
                })
                .Build();

        var production = Mock.Of<IHostEnvironment>(environment =>
            environment.EnvironmentName == Environments.Production);
        var invalidServices = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            OutboxDispatcherWorker.RegisterUnlessExplicitlyDisabledForTesting(
                invalidServices,
                Configuration(disabled: true),
                production));
        Assert.DoesNotContain(invalidServices, descriptor =>
            descriptor.ServiceType == typeof(IHostedService));

        var testing = Mock.Of<IHostEnvironment>(environment =>
            environment.EnvironmentName == "Testing");
        var receiverOnlyServices = new ServiceCollection();
        Assert.True(OutboxDispatcherWorker.RegisterUnlessExplicitlyDisabledForTesting(
            receiverOnlyServices,
            Configuration(disabled: true),
            testing));
        Assert.DoesNotContain(receiverOnlyServices, descriptor =>
            descriptor.ServiceType == typeof(IHostedService));

        var normalServices = new ServiceCollection();
        Assert.False(OutboxDispatcherWorker.RegisterUnlessExplicitlyDisabledForTesting(
            normalServices,
            Configuration(disabled: false),
            production));
        Assert.Contains(normalServices, descriptor =>
            descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void OutboxIdentityAndInboxDefaults_ShouldFailClosedAndUsePostgresRetention()
    {
        var context = new DomainEventDispatchContext();
        Assert.Throws<InvalidOperationException>(() => context.MessageId);
        Assert.Throws<ArgumentException>(() => context.Enter(Guid.Empty));

        var messageId = Guid.NewGuid();
        var scope = context.Enter(messageId);
        Assert.Equal(messageId, context.MessageId);
        Assert.Throws<InvalidOperationException>(() => context.Enter(Guid.NewGuid()));
        scope.Dispose();
        scope.Dispose();
        Assert.Throws<InvalidOperationException>(() => context.MessageId);

        Assert.Throws<ArgumentNullException>(() =>
            IntegrationEventInboxDefaults.ConfigurePostgres(null!));
        var configurator = new Mock<IEntityFrameworkOutboxConfigurator>();
        configurator.SetupAllProperties();
        IntegrationEventInboxDefaults.ConfigurePostgres(configurator.Object);
        configurator.VerifySet(value =>
            value.DuplicateDetectionWindow = IntegrationEventInboxDefaults.DuplicateDetectionWindow);
        configurator.VerifySet(value =>
            value.QueryDelay = IntegrationEventInboxDefaults.QueryDelay);
    }

    [Theory]
    [InlineData("/api/v1/human/devices", "human", "human", false)]
    [InlineData("/api/v1/edge/device-logs", "edge", "edge", false)]
    [InlineData("/api/v1/edge/bootstrap/device-instance", "blocked-bootstrap-alias", "blocked", true)]
    [InlineData("/unknown", "unknown", "unmatched", false)]
    public void GatewayRouteCatalog_ShouldResolveConfiguredSurfacesAndRejectAliases(
        string path,
        string expectedSurface,
        string expectedRouteOrUpstream,
        bool expectedBlocked)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GatewayRoutes:BlockedAliases:blocked-edge-bootstrap:PathPrefix"] = "/api/v1/edge/bootstrap",
            ["GatewayRoutes:BlockedAliases:blocked-edge-bootstrap:RouteSurface"] = "blocked-bootstrap-alias",
            ["ReverseProxy:Routes:human:ClusterId"] = "httpapi",
            ["ReverseProxy:Routes:human:Match:Path"] = "/api/v1/human/{**catch-all}",
            ["ReverseProxy:Routes:human:Transforms:0:RequestHeader"] = GatewayRouteCatalog.RouteSurfaceHeader,
            ["ReverseProxy:Routes:human:Transforms:0:Set"] = "human",
            ["ReverseProxy:Routes:edge:ClusterId"] = "httpapi",
            ["ReverseProxy:Routes:edge:Match:Path"] = "/api/v1/edge/{**catch-all}",
            ["ReverseProxy:Routes:edge:Transforms:0:RequestHeader"] = GatewayRouteCatalog.RouteSurfaceHeader,
            ["ReverseProxy:Routes:edge:Transforms:0:Set"] = "edge"
        }).Build();
        var catalog = new GatewayRouteCatalog(configuration);

        var route = catalog.Resolve(path);

        Assert.Equal(expectedSurface, route.RouteSurface);
        Assert.Equal(expectedBlocked, route.IsBlockedAlias);
        if (expectedBlocked)
        {
            Assert.Equal(expectedRouteOrUpstream, route.UpstreamCluster);
        }
        else if (expectedSurface == "unknown")
        {
            Assert.Equal(expectedRouteOrUpstream, route.MatchedRoute);
        }
        else
        {
            Assert.Equal("httpapi", route.UpstreamCluster);
            Assert.Equal(expectedRouteOrUpstream, route.MatchedRoute);
        }
    }

    [Fact]
    public async Task HourlyCapacityConsumer_ShouldRejectUnsupportedSchemaBeforeDispatch()
    {
        var sender = new Mock<ISender>(MockBehavior.Strict);
        var consumer = new HourlyCapacityConsumer(sender.Object);
        var context = CreateContext(new HourlyCapacityReceivedEvent { SchemaVersion = 2 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context));
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeviceLogConsumer_ShouldRejectUnsupportedSchemaBeforeDispatch()
    {
        var sender = new Mock<ISender>(MockBehavior.Strict);
        var consumer = new DeviceLogConsumer(sender.Object);
        var context = CreateContext(new DeviceLogReceivedEvent { SchemaVersion = 0 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context));
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PassStationConsumer_ShouldRejectUnsupportedSchemaBeforeDispatch()
    {
        var sender = new Mock<ISender>(MockBehavior.Strict);
        var consumer = new PassStationConsumer(sender.Object);
        var context = CreateContext(new PassStationBatchReceivedEvent { SchemaVersion = 2 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context));
        sender.VerifyNoOtherCalls();
    }

    private static ConsumeContext<T> CreateContext<T>(T message)
        where T : class =>
        Mock.Of<ConsumeContext<T>>(context =>
            context.Message == message && context.CancellationToken == CancellationToken.None);
}
