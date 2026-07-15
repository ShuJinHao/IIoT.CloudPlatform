using System.Collections.Concurrent;
using System.Data.Common;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Production.Aggregates.Recipes.Events;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EventBus;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Caching;
using IIoT.ProductionService.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Configuration;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

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

    [Fact]
    public async Task OutboxCommitTransient_ShouldRepublishStableIdentityAndReceiverInboxApplyBusinessEffectOnce()
    {
        Assert.True(
            fixture.DataWorkerOutboxDispatcherDisabled,
            "This test requires only the outbound DataWorker dispatcher to be disabled; its real receivers remain running.");
        var connectionString = await fixture.GetConnectionStringAsync();
        var eventBusConnectionString = await fixture.GetConnectionStringAsync(ConnectionResourceNames.EventBus);
        var deviceId = Guid.NewGuid();
        var integrationEvent = CreateHourlyCapacityEvent(deviceId, minute: 0);
        var recentEvent = CreateHourlyCapacityEvent(deviceId, minute: 30);
        var persistedMessage = OutboxMessage.FromIntegrationEvent(integrationEvent);
        var messageId = persistedMessage.Id;
        var recentMessageId = Guid.NewGuid();

        await PrepareHourlyCapacityDeviceAsync(connectionString, deviceId);
        var bus = Bus.Factory.CreateUsingRabbitMq(configuration =>
            configuration.Host(eventBusConnectionString));
        await bus.StartAsync();
        try
        {
            var commitTransient = new ThrowOnceBeforeCommitInterceptor();
            var senderServices = new ServiceCollection();
            senderServices.AddLogging();
            senderServices.AddDbContext<IIoTDbContext>(options =>
                options.UseNpgsql(
                        connectionString,
                        npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(50), null))
                    .AddInterceptors(commitTransient));

            await using var senderProvider = senderServices.BuildServiceProvider();
            await using var senderScope = senderProvider.CreateAsyncScope();
            var senderDbContext = senderScope.ServiceProvider.GetRequiredService<IIoTDbContext>();
            await senderDbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
            senderDbContext.OutboxMessages.Add(persistedMessage);
            await senderDbContext.SaveChangesAsync();

            var transportPublisher = new MassTransitEventPublisher(bus);
            var publisher = new CountingEventPublisher(transportPublisher);
            var dispatcher = CreateDispatcher(senderDbContext, new NoopMediator(), publisher);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            InboxAndBusinessObservation? firstReceiverObservation = null;
            commitTransient.Arm(async cancellationToken =>
            {
                firstReceiverObservation = await WaitForInboxAndHourlyCapacityAsync(
                    connectionString,
                    messageId,
                    integrationEvent,
                    expectedReceiveCount: 1,
                    cancellationToken);
            });

            var dispatch = await dispatcher.DispatchPendingAsync(timeout.Token);
            Assert.NotNull(firstReceiverObservation);
            var observation = await WaitForInboxAndHourlyCapacityAsync(
                connectionString,
                messageId,
                integrationEvent,
                expectedReceiveCount: firstReceiverObservation.ReceiveCount + 1,
                timeout.Token);

            await transportPublisher.PublishAsync(recentEvent, recentMessageId, timeout.Token);
            var recentObservation = await WaitForInboxAndHourlyCapacityAsync(
                connectionString,
                recentMessageId,
                recentEvent,
                expectedReceiveCount: 1,
                timeout.Token);

            senderDbContext.ChangeTracker.Clear();
            var processedMessage = await senderDbContext.OutboxMessages.SingleAsync(timeout.Token);

            Assert.Equal(1, commitTransient.ExceptionsThrown);
            Assert.Equal(2, publisher.MessageIds.Count);
            Assert.All(publisher.MessageIds, publishedMessageId => Assert.Equal(messageId, publishedMessageId));
            Assert.Equal(1, dispatch.ScannedCount);
            Assert.Equal(1, dispatch.SucceededCount);
            Assert.NotNull(processedMessage.ProcessedAtUtc);
            Assert.Equal(2, processedMessage.AttemptCount);
            Assert.True(observation.ReceiveCount > firstReceiverObservation.ReceiveCount);
            Assert.NotNull(observation.Consumed);
            Assert.NotNull(observation.Delivered);
            Assert.Equal(1, observation.BusinessRowCount);
            Assert.Equal(1, recentObservation.BusinessRowCount);
            Assert.NotNull(recentObservation.Delivered);

            await AgeInboxStatePastDuplicateWindowAsync(
                connectionString,
                messageId,
                timeout.Token);
            await WaitForRealInboxCleanupAsync(
                connectionString,
                expiredMessageId: messageId,
                retainedMessageId: recentMessageId,
                timeout.Token);
        }
        finally
        {
            await bus.StopAsync();
            await CleanupHourlyCapacityScenarioAsync(
                connectionString,
                deviceId,
                [messageId, recentMessageId]);
        }
    }

    [Fact]
    public async Task DomainEventCommitTransient_StableOutboxIdentityKeepsRealRedisInvalidationOnce()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var redisConnectionString = await fixture.GetConnectionStringAsync("redis-cache");
        var domainEvent = new RecipeCreatedDomainEvent(
            Guid.NewGuid(),
            "Domain Event Cache Test",
            "V1.0",
            Guid.NewGuid(),
            Guid.NewGuid());
        var persistedMessage = OutboxMessage.FromDomainEvent(domainEvent);
        var recipeKey = CacheKeys.Recipe(domainEvent.RecipeId);
        var processKey = CacheKeys.RecipesByProcess(domainEvent.ProcessId);
        var deviceKey = CacheKeys.RecipesByDevice(domainEvent.DeviceId);
        var receiptKey = RedisCacheService.GetDomainEventInvalidationReceiptKey(
            persistedMessage.Id,
            "recipe-change");
        var commitTransient = new ThrowOnceBeforeCommitInterceptor();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:redis-cache"] = redisConnectionString
        });
        builder.AddInfrastructures();
        builder.Services.AddDbContext<IIoTDbContext>(options =>
            options.UseNpgsql(
                    connectionString,
                    npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(50), null))
                .AddInterceptors(commitTransient));
        builder.Services.AddScoped<DomainEventDispatchContext>();
        builder.Services.AddScoped<IDomainEventDispatchContext>(provider =>
            provider.GetRequiredService<DomainEventDispatchContext>());
        builder.Services.AddScoped<IRecipeCacheInvalidationService, RecipeCacheInvalidationService>();
        builder.Services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<RecipeCreatedCacheInvalidationHandler>());

        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var fusionCache = scope.ServiceProvider.GetRequiredService<IFusionCache>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var database = redis.GetDatabase();
        await database.KeyDeleteAsync(receiptKey);
        await fusionCache.SetAsync(recipeKey, "before-domain-event");
        await fusionCache.SetAsync(processKey, "before-domain-event");
        await fusionCache.SetAsync(deviceKey, "before-domain-event");
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
        dbContext.OutboxMessages.Add(persistedMessage);
        await dbContext.SaveChangesAsync();

        var mediator = new CountingMediator(scope.ServiceProvider.GetRequiredService<IMediator>());
        var dispatcher = CreateDispatcher(
            dbContext,
            mediator,
            new RecordingEventPublisher(),
            domainEventDispatchContext: scope.ServiceProvider.GetRequiredService<DomainEventDispatchContext>());
        commitTransient.Arm(async cancellationToken =>
            await fusionCache.SetAsync(
                recipeKey,
                "reseeded-after-first-invalidation",
                token: cancellationToken));

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var dispatch = await dispatcher.DispatchPendingAsync(timeout.Token);
            dbContext.ChangeTracker.Clear();
            var processed = await dbContext.OutboxMessages.SingleAsync(timeout.Token);

            Assert.Equal(1, commitTransient.ExceptionsThrown);
            Assert.Equal(2, mediator.DomainEventPublishCount);
            Assert.Equal(1, dispatch.SucceededCount);
            Assert.NotNull(processed.ProcessedAtUtc);
            Assert.Equal(
                "reseeded-after-first-invalidation",
                (await fusionCache.TryGetAsync<string>(recipeKey, token: timeout.Token)).Value);
            Assert.False((await fusionCache.TryGetAsync<string>(
                processKey,
                token: timeout.Token)).HasValue);
            Assert.False((await fusionCache.TryGetAsync<string>(
                deviceKey,
                token: timeout.Token)).HasValue);
            Assert.Equal("completed", await database.StringGetAsync(receiptKey));
            var receiptTtl = await database.KeyTimeToLiveAsync(receiptKey);
            Assert.NotNull(receiptTtl);
            Assert.InRange(receiptTtl!.Value, TimeSpan.FromDays(29), TimeSpan.FromDays(30));
        }
        finally
        {
            await database.KeyDeleteAsync([receiptKey, recipeKey, processKey, deviceKey]);
            await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_messages");
        }
    }

    private static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.Create(_ => { }).CreateLogger<T>();
    }

    private static async Task PrepareHourlyCapacityDeviceAsync(
        string connectionString,
        Guid deviceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO devices (id, device_name, client_code, process_id)
            VALUES (@device_id, @device_name, @client_code, @process_id)
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("device_name", $"ACK lost {deviceId:N}");
        command.Parameters.AddWithValue("client_code", $"ACK-{deviceId:N}");
        command.Parameters.AddWithValue("process_id", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<InboxAndBusinessObservation> WaitForInboxAndHourlyCapacityAsync(
        string connectionString,
        Guid messageId,
        HourlyCapacityReceivedEvent integrationEvent,
        int expectedReceiveCount,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var inboxCommand = connection.CreateCommand();
                inboxCommand.CommandText = """
                    SELECT "ReceiveCount", "Consumed", "Delivered"
                    FROM integration_event_inbox_states
                    WHERE "MessageId" = @message_id
                    LIMIT 1;
                    """;
                inboxCommand.Parameters.AddWithValue("message_id", messageId);
                await using var reader = await inboxCommand.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var observation = new InboxAndBusinessObservation(
                        reader.GetInt32(0),
                        reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                        reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                        0);
                    await reader.DisposeAsync();

                    await using var businessCommand = connection.CreateCommand();
                    businessCommand.CommandText = """
                        SELECT COUNT(*)
                        FROM hourly_capacity
                        WHERE device_id = @device_id
                          AND date = @date
                          AND shift_code = @shift_code
                          AND hour = @hour
                          AND minute = @minute
                          AND plc_name = @plc_name
                          AND total_count = @total_count
                          AND ok_count = @ok_count
                          AND ng_count = @ng_count;
                        """;
                    businessCommand.Parameters.AddWithValue("device_id", integrationEvent.DeviceId);
                    businessCommand.Parameters.AddWithValue("date", integrationEvent.Date);
                    businessCommand.Parameters.AddWithValue("shift_code", integrationEvent.ShiftCode);
                    businessCommand.Parameters.AddWithValue("hour", integrationEvent.Hour);
                    businessCommand.Parameters.AddWithValue("minute", integrationEvent.Minute);
                    businessCommand.Parameters.AddWithValue("plc_name", integrationEvent.PlcName ?? string.Empty);
                    businessCommand.Parameters.AddWithValue("total_count", integrationEvent.TotalCount);
                    businessCommand.Parameters.AddWithValue("ok_count", integrationEvent.OkCount);
                    businessCommand.Parameters.AddWithValue("ng_count", integrationEvent.NgCount);
                    var businessCount = Convert.ToInt32(
                        await businessCommand.ExecuteScalarAsync(cancellationToken));
                    observation = observation with { BusinessRowCount = businessCount };
                    if (observation.ReceiveCount >= expectedReceiveCount &&
                        observation.Consumed.HasValue &&
                        observation.Delivered.HasValue &&
                        observation.BusinessRowCount == 1)
                    {
                        return observation;
                    }
                }
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                lastFailure = ex;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"Formal DataWorker hourly-capacity receiver did not persist message {messageId}: {lastFailure?.Message}");
    }

    private static async Task AgeInboxStatePastDuplicateWindowAsync(
        string connectionString,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE integration_event_inbox_states
            SET "Delivered" = @expired_at,
                "Consumed" = @expired_at
            WHERE "MessageId" = @message_id;
            """;
        command.Parameters.AddWithValue(
            "expired_at",
            DateTime.UtcNow - IntegrationEventInboxDefaults.DuplicateDetectionWindow - TimeSpan.FromMinutes(5));
        command.Parameters.AddWithValue("message_id", messageId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
    }

    private static async Task WaitForRealInboxCleanupAsync(
        string connectionString,
        Guid expiredMessageId,
        Guid retainedMessageId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    COUNT(*) FILTER (WHERE "MessageId" = @expired_message_id),
                    COUNT(*) FILTER (WHERE "MessageId" = @retained_message_id)
                FROM integration_event_inbox_states
                WHERE "MessageId" IN (@expired_message_id, @retained_message_id);
                """;
            command.Parameters.AddWithValue("expired_message_id", expiredMessageId);
            command.Parameters.AddWithValue("retained_message_id", retainedMessageId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            if (reader.GetInt64(0) == 0 && reader.GetInt64(1) == 1)
                return;

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("The real DataWorker inbox cleanup did not remove only the expired seven-day row.");
    }

    private static async Task CleanupHourlyCapacityScenarioAsync(
        string connectionString,
        Guid deviceId,
        IReadOnlyCollection<Guid> messageIds)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM consumer_outbox_messages WHERE "InboxMessageId" = ANY(@message_ids);
            DELETE FROM integration_event_inbox_states WHERE "MessageId" = ANY(@message_ids);
            DELETE FROM hourly_capacity WHERE device_id = @device_id;
            DELETE FROM devices WHERE id = @device_id;
            DELETE FROM outbox_messages;
            """;
        command.Parameters.AddWithValue("message_ids", messageIds.ToArray());
        command.Parameters.AddWithValue("device_id", deviceId);
        await command.ExecuteNonQueryAsync();
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

    private static HourlyCapacityReceivedEvent CreateHourlyCapacityEvent(
        Guid? deviceId = null,
        int minute = 0)
    {
        return new HourlyCapacityReceivedEvent
        {
            DeviceId = deviceId ?? Guid.NewGuid(),
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftCode = "D",
            Hour = 10,
            Minute = minute,
            TimeLabel = $"10:{minute:00}",
            TotalCount = 20,
            OkCount = 19,
            NgCount = 1,
            PlcName = "ACK-LOST-PLC",
            ReceivedAtUtc = DateTime.UtcNow
        };
    }

    private static OutboxMessageDispatcher CreateDispatcher(
        IIoTDbContext dbContext,
        IMediator mediator,
        IEventPublisher eventPublisher,
        int maxAttempts = 5,
        DomainEventDispatchContext? domainEventDispatchContext = null)
    {
        return new OutboxMessageDispatcher(
            dbContext,
            mediator,
            eventPublisher,
            domainEventDispatchContext ?? new DomainEventDispatchContext(),
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

    private sealed record InboxAndBusinessObservation(
        int ReceiveCount,
        DateTime? Consumed,
        DateTime? Delivered,
        int BusinessRowCount);

    private sealed class ThrowOnceBeforeCommitInterceptor : DbTransactionInterceptor
    {
        private int armed;
        private int exceptionsThrown;
        private Func<CancellationToken, Task>? beforeThrow;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm(Func<CancellationToken, Task>? beforeThrow = null)
        {
            this.beforeThrow = beforeThrow;
            Volatile.Write(ref armed, 1);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                if (beforeThrow is not null)
                    await beforeThrow(cancellationToken);
                Interlocked.Increment(ref exceptionsThrown);
                throw new PostgresException(
                    "simulated sender commit transient after external publish",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.SerializationFailure);
            }

            return result;
        }
    }

    private sealed class CountingMediator(IMediator inner) : IMediator
    {
        private int domainEventPublishCount;

        public int DomainEventPublishCount => Volatile.Read(ref domainEventPublishCount);

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            inner.Send(request, cancellationToken);

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            inner.Send(request, cancellationToken);

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
            inner.Send(request, cancellationToken);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            inner.CreateStream(request, cancellationToken);

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            inner.CreateStream(request, cancellationToken);

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref domainEventPublishCount);
            return inner.Publish(notification, cancellationToken);
        }

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Interlocked.Increment(ref domainEventPublishCount);
            return inner.Publish(notification, cancellationToken);
        }
    }

    private sealed class CountingEventPublisher(IEventPublisher inner) : IEventPublisher
    {
        private readonly ConcurrentQueue<Guid> messageIds = new();

        public IReadOnlyCollection<Guid> MessageIds => messageIds.ToArray();

        public Task PublishAsync(
            IIntegrationEvent @event,
            Guid messageId,
            CancellationToken cancellationToken = default)
        {
            messageIds.Enqueue(messageId);
            return inner.PublishAsync(@event, messageId, cancellationToken);
        }

    }

}
