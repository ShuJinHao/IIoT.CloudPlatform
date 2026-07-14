using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.Capacities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class OutboxDispatchPersistenceTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task OutboxMessageDispatcher_ShouldPublishOnceAcrossConcurrentPostgresWorkers()
    {
        var firstMediator = new ReleasableMediator();
        var secondMediator = new RecordingMediator();
        using var firstScope = await CreateTestScopeAsync(firstMediator);
        using var secondScope = await CreateTestScopeAsync(secondMediator);
        var dbContext = firstScope.DbContext;
        TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"E1002-{Guid.NewGuid():N}",
            "Dispatcher");
        await dbContext.SaveChangesAsync();

        var firstDispatcher = CreateDispatcher(dbContext, firstMediator, new RecordingEventPublisher());
        var secondDispatcher = CreateDispatcher(
            secondScope.DbContext,
            secondMediator,
            new RecordingEventPublisher());

        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstDispatchTask = firstDispatcher.DispatchPendingAsync(testTimeout.Token);
        await firstMediator.WaitUntilPublishEnteredAsync(testTimeout.Token);
        var secondDispatch = await secondDispatcher.DispatchPendingAsync(testTimeout.Token);
        firstMediator.Release();
        var firstDispatch = await firstDispatchTask;
        dbContext.ChangeTracker.Clear();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Equal(1, firstDispatch.ScannedCount);
        Assert.Equal(1, firstDispatch.SucceededCount);
        Assert.Equal(0, firstDispatch.FailedCount);
        Assert.Equal(0, firstDispatch.PendingBacklogCount);
        Assert.Null(firstDispatch.LastFailureSummary);
        Assert.Equal(0, secondDispatch.ScannedCount);
        Assert.Equal(0, secondDispatch.SucceededCount);
        Assert.Equal(1, secondDispatch.PendingBacklogCount);
        Assert.Empty(secondMediator.PublishedNotifications);
        Assert.Single(firstMediator.PublishedNotifications);
        Assert.IsType<EmployeeOnboardedDomainEvent>(firstMediator.PublishedNotifications[0]);
        Assert.NotNull(outboxMessage.ProcessedAtUtc);
        Assert.Null(outboxMessage.LastError);
        Assert.Equal(1, outboxMessage.AttemptCount);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldPublishIntegrationMessagesAndMarkProcessed()
    {
        using var testScope = await CreateTestScopeAsync(new NoopMediator());
        var dbContext = testScope.DbContext;
        var publisher = new RecordingEventPublisher();
        dbContext.OutboxMessages.Add(OutboxMessage.FromIntegrationEvent(CreateHourlyCapacityEvent()));
        await dbContext.SaveChangesAsync();

        var (dispatched, outboxMessage) = await DispatchSingleAsync(testScope, publisher);

        Assert.Equal(1, dispatched.ScannedCount);
        Assert.Equal(1, dispatched.SucceededCount);
        Assert.Equal(0, dispatched.FailedCount);
        Assert.IsType<HourlyCapacityReceivedEvent>(publisher.LastPublishedEvent);
        Assert.NotNull(outboxMessage.ProcessedAtUtc);
        Assert.Equal(1, outboxMessage.AttemptCount);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldKeepFailedIntegrationMessagesPending()
    {
        using var testScope = await CreateTestScopeAsync(new NoopMediator());
        var dbContext = testScope.DbContext;
        dbContext.OutboxMessages.Add(OutboxMessage.FromIntegrationEvent(CreateHourlyCapacityEvent()));
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingEventPublisher(
            publishException: new InvalidOperationException("publish failed"));
        var (dispatched, outboxMessage) = await DispatchSingleAsync(testScope, publisher);

        AssertFailedPending(dispatched, outboxMessage, "publish failed");
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldKeepFailedMessagesPending()
    {
        using var testScope = await CreateTestScopeAsync(new ThrowingMediator("dispatch failed"));
        var dbContext = testScope.DbContext;
        TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"E1003-{Guid.NewGuid():N}",
            "Dispatcher Failure");
        await dbContext.SaveChangesAsync();

        var (dispatched, outboxMessage) = await DispatchSingleAsync(
            testScope,
            new RecordingEventPublisher());

        AssertFailedPending(dispatched, outboxMessage, "dispatch failed");
        Assert.NotNull(outboxMessage.LastAttemptedAtUtc);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_CallerCancellation_ShouldPropagateAndRollbackTransaction()
    {
        var cancellationMediator = new ReleasableMediator();
        using var cancellationScope = await CreateTestScopeAsync(cancellationMediator);
        TestIdentityData.AddEmployeeWithIdentity(
            cancellationScope.DbContext,
            $"E1005-{Guid.NewGuid():N}",
            "Dispatcher Cancellation");
        await cancellationScope.DbContext.SaveChangesAsync();
        var cancellationDispatcher = CreateDispatcher(
            cancellationScope.DbContext,
            cancellationMediator,
            new RecordingEventPublisher());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var cancellationTask = cancellationDispatcher.DispatchPendingAsync(cancellation.Token);
        await cancellationMediator.WaitUntilPublishEnteredAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellationTask);
        cancellationScope.DbContext.ChangeTracker.Clear();
        var cancelledMessage = await cancellationScope.DbContext.OutboxMessages.SingleAsync();
        Assert.Null(cancelledMessage.ProcessedAtUtc);
        Assert.Null(cancelledMessage.LastAttemptedAtUtc);
        Assert.Null(cancelledMessage.LastError);
        Assert.Equal(0, cancelledMessage.AttemptCount);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldMarkMessagesAbandonedAtMaxAttempts()
    {
        using var testScope = await CreateTestScopeAsync(new ThrowingMediator("dispatch failed"));
        var dbContext = testScope.DbContext;
        TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"E1004-{Guid.NewGuid():N}",
            "Dispatcher Exhausted");
        await dbContext.SaveChangesAsync();

        var dispatcher = CreateDispatcher(
            dbContext,
            testScope.Mediator,
            new RecordingEventPublisher(),
            maxAttempts: 1);

        var firstDispatch = await dispatcher.DispatchPendingAsync();
        var secondDispatch = await dispatcher.DispatchPendingAsync();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Equal(1, firstDispatch.ScannedCount);
        Assert.Equal(1, firstDispatch.FailedCount);
        Assert.Equal(1, firstDispatch.PendingBacklogCount);
        Assert.Equal(1, firstDispatch.AbandonedCount);
        Assert.Equal(0, secondDispatch.ScannedCount);
        Assert.Equal(1, secondDispatch.PendingBacklogCount);
        Assert.Equal(1, secondDispatch.AbandonedCount);
        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.NotNull(outboxMessage.AbandonedAtUtc);
        Assert.True(outboxMessage.IsAbandoned);
        Assert.Equal(1, outboxMessage.AttemptCount);
        Assert.Equal("dispatch failed", outboxMessage.LastError);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldReturnEmptyCycleStatisticsWhenNothingIsPending()
    {
        using var testScope = await CreateTestScopeAsync(new RecordingMediator());
        var dispatcher = CreateDispatcher(testScope.DbContext, testScope.Mediator, new RecordingEventPublisher());

        var dispatched = await dispatcher.DispatchPendingAsync();

        Assert.Equal(0, dispatched.ScannedCount);
        Assert.Equal(0, dispatched.SucceededCount);
        Assert.Equal(0, dispatched.FailedCount);
        Assert.Equal(0, dispatched.PendingBacklogCount);
        Assert.Equal(0, dispatched.AbandonedCount);
        Assert.Null(dispatched.LastFailureSummary);
    }

    private static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.Create(_ => { }).CreateLogger<T>();
    }

    private static async Task<(OutboxDispatchResult Dispatch, OutboxMessage Message)> DispatchSingleAsync(
        OutboxTestScope scope,
        IEventPublisher publisher)
    {
        var dispatcher = CreateDispatcher(scope.DbContext, scope.Mediator, publisher);
        var dispatch = await dispatcher.DispatchPendingAsync();
        var message = await scope.DbContext.OutboxMessages.SingleAsync();
        return (dispatch, message);
    }

    private static void AssertFailedPending(
        OutboxDispatchResult dispatch,
        OutboxMessage message,
        string expectedFailure)
    {
        Assert.Equal(1, dispatch.ScannedCount);
        Assert.Equal(0, dispatch.SucceededCount);
        Assert.Equal(1, dispatch.FailedCount);
        Assert.Equal(1, dispatch.PendingBacklogCount);
        Assert.Equal(0, dispatch.AbandonedCount);
        Assert.NotNull(dispatch.LastFailureSummary);
        Assert.Contains(expectedFailure, dispatch.LastFailureSummary, StringComparison.Ordinal);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(expectedFailure, message.LastError);
    }

    private static HourlyCapacityReceivedEvent CreateHourlyCapacityEvent()
    {
        return new HourlyCapacityReceivedEvent
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
    }

    private static OutboxMessageDispatcher CreateDispatcher(
        IIoTDbContext dbContext,
        IMediator mediator,
        IEventPublisher eventPublisher,
        int maxAttempts = 5)
    {
        return new OutboxMessageDispatcher(
            dbContext,
            mediator,
            eventPublisher,
            Options.Create(new OutboxDispatcherOptions
            {
                BatchSize = 10,
                PollingIntervalSeconds = 1,
                MaxAttempts = maxAttempts
            }),
            CreateLogger<OutboxMessageDispatcher>());
    }

    private async Task<ServiceProvider> CreateServiceProviderAsync(IMediator mediator)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mediator);
        services.AddSingleton<IMediator>(mediator);
        services.AddDbContext<IIoTDbContext>(options =>
            options.UseNpgsql(connectionString));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
        return provider;
    }

    private async Task<OutboxTestScope> CreateTestScopeAsync(IMediator mediator)
    {
        var provider = await CreateServiceProviderAsync(mediator);
        return new OutboxTestScope(provider, mediator);
    }

    private sealed class OutboxTestScope : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public OutboxTestScope(ServiceProvider provider, IMediator mediator)
        {
            _provider = provider;
            _scope = provider.CreateScope();
            Mediator = mediator;
            DbContext = _scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        }

        public IIoTDbContext DbContext { get; }

        public IMediator Mediator { get; }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class ReleasableMediator : IMediator
    {
        private readonly TaskCompletionSource<bool> publishEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<object> PublishedNotifications { get; } = [];

        public Task WaitUntilPublishEnteredAsync(CancellationToken cancellationToken) =>
            publishEntered.Task.WaitAsync(cancellationToken);

        public void Release() => release.TrySetResult(true);

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            PublishedNotifications.Add(notification);
            publishEntered.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
        }

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification!, cancellationToken);
    }

}
