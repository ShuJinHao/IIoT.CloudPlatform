using System.IO;
using FluentValidation;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.EventBus;
using IIoT.IdentityService.Commands;
using IIoT.Infrastructure.Logging;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.SharedKernel.Configuration;
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

namespace IIoT.ServiceLayer.Tests;

public sealed class InfrastructureBehaviorTests
{
    private const string RefreshTokenHeaderName = "X-IIoT-Refresh-Token";
    private const string RefreshTokenExpiresAtHeaderName = "X-IIoT-Refresh-Token-Expires-At";

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
    public async Task DbContext_SaveChanges_ShouldPersistDomainEventsToOutbox()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = new Employee(Guid.NewGuid(), "E1001", "Operator");

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Contains(nameof(EmployeeOnboardedDomainEvent), outboxMessage.EventType);
        Assert.Contains("E1001", outboxMessage.Payload, StringComparison.Ordinal);
        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.Empty(employee.DomainEvents);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldPublishAndMarkMessagesProcessed()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new RecordingMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var mediator = (RecordingMediator)scope.ServiceProvider.GetRequiredService<IMediator>();
        var employee = new Employee(Guid.NewGuid(), "E1002", "Dispatcher");

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var dispatcher = new OutboxMessageDispatcher(
            dbContext,
            mediator,
            Options.Create(new OutboxDispatcherOptions
            {
                BatchSize = 10,
                PollingIntervalSeconds = 1
            }),
            CreateLogger<OutboxMessageDispatcher>());

        var dispatched = await dispatcher.DispatchPendingAsync();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Equal(1, dispatched.ScannedCount);
        Assert.Equal(1, dispatched.SucceededCount);
        Assert.Equal(0, dispatched.FailedCount);
        Assert.Equal(0, dispatched.PendingBacklogCount);
        Assert.Null(dispatched.LastFailureSummary);
        Assert.Single(mediator.PublishedNotifications);
        Assert.IsType<EmployeeOnboardedDomainEvent>(mediator.PublishedNotifications[0]);
        Assert.NotNull(outboxMessage.ProcessedAtUtc);
        Assert.Null(outboxMessage.LastError);
        Assert.Equal(1, outboxMessage.AttemptCount);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldKeepFailedMessagesPending()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new ThrowingMediator("dispatch failed"));
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        dbContext.Employees.Add(new Employee(Guid.NewGuid(), "E1003", "Dispatcher Failure"));
        await dbContext.SaveChangesAsync();

        var dispatcher = new OutboxMessageDispatcher(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IMediator>(),
            Options.Create(new OutboxDispatcherOptions
            {
                BatchSize = 10,
                PollingIntervalSeconds = 1
            }),
            CreateLogger<OutboxMessageDispatcher>());

        var dispatched = await dispatcher.DispatchPendingAsync();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Equal(1, dispatched.ScannedCount);
        Assert.Equal(0, dispatched.SucceededCount);
        Assert.Equal(1, dispatched.FailedCount);
        Assert.Equal(1, dispatched.PendingBacklogCount);
        Assert.NotNull(dispatched.LastFailureSummary);
        Assert.Contains("dispatch failed", dispatched.LastFailureSummary, StringComparison.Ordinal);
        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.Equal(1, outboxMessage.AttemptCount);
        Assert.Equal("dispatch failed", outboxMessage.LastError);
        Assert.NotNull(outboxMessage.LastAttemptedAtUtc);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldSkipMessagesAtMaxAttempts()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new ThrowingMediator("dispatch failed"));
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        dbContext.Employees.Add(new Employee(Guid.NewGuid(), "E1004", "Dispatcher Exhausted"));
        await dbContext.SaveChangesAsync();

        var dispatcher = new OutboxMessageDispatcher(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IMediator>(),
            Options.Create(new OutboxDispatcherOptions
            {
                BatchSize = 10,
                PollingIntervalSeconds = 1,
                MaxAttempts = 1
            }),
            CreateLogger<OutboxMessageDispatcher>());

        var firstDispatch = await dispatcher.DispatchPendingAsync();
        var secondDispatch = await dispatcher.DispatchPendingAsync();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Equal(1, firstDispatch.ScannedCount);
        Assert.Equal(1, firstDispatch.FailedCount);
        Assert.Equal(0, firstDispatch.PendingBacklogCount);
        Assert.Equal(0, secondDispatch.ScannedCount);
        Assert.Equal(0, secondDispatch.PendingBacklogCount);
        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.Equal(1, outboxMessage.AttemptCount);
        Assert.Equal("dispatch failed", outboxMessage.LastError);
    }

    [Fact]
    public async Task OutboxMessageDispatcher_ShouldReturnEmptyCycleStatisticsWhenNothingIsPending()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new RecordingMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var dispatcher = new OutboxMessageDispatcher(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IMediator>(),
            Options.Create(new OutboxDispatcherOptions
            {
                BatchSize = 10,
                PollingIntervalSeconds = 1
            }),
            CreateLogger<OutboxMessageDispatcher>());

        var dispatched = await dispatcher.DispatchPendingAsync();

        Assert.Equal(0, dispatched.ScannedCount);
        Assert.Equal(0, dispatched.SucceededCount);
        Assert.Equal(0, dispatched.FailedCount);
        Assert.Equal(0, dispatched.PendingBacklogCount);
        Assert.Null(dispatched.LastFailureSummary);
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
    public void SensitiveDataDestructuringPolicy_ShouldRedactLoginSessionAndRefreshTokenValues()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var loginCommand = new EdgeOperatorLoginCommand("E1001", "plain-password", Guid.NewGuid());
        var session = new HumanIdentitySessionResult(
            "access-token-value",
            DateTimeOffset.UtcNow.AddMinutes(30),
            "refresh-token-value",
            DateTimeOffset.UtcNow.AddDays(7));
        var headers = new Dictionary<string, string?>
        {
            [RefreshTokenHeaderName] = "refresh-header-value",
            [RefreshTokenExpiresAtHeaderName] = DateTimeOffset.UtcNow.AddDays(7).ToString("O")
        };

        logger.Information(
            "Login {@Login} Session {@Session} Headers {@Headers}",
            loginCommand,
            session,
            headers);

        var logEvent = Assert.Single(sink.Events);
        var loginValue = Assert.IsType<StructureValue>(logEvent.Properties["Login"]);
        var sessionValue = Assert.IsType<StructureValue>(logEvent.Properties["Session"]);
        var headerValue = Assert.IsType<DictionaryValue>(logEvent.Properties["Headers"]);

        Assert.Equal(
            SensitiveDataDestructuringPolicy.RedactedValue,
            GetScalarValue(loginValue, nameof(EdgeOperatorLoginCommand.Password)));
        Assert.Equal(
            SensitiveDataDestructuringPolicy.RedactedValue,
            GetScalarValue(sessionValue, nameof(HumanIdentitySessionResult.AccessToken)));
        Assert.Equal(
            SensitiveDataDestructuringPolicy.RedactedValue,
            GetScalarValue(sessionValue, nameof(HumanIdentitySessionResult.RefreshToken)));

        var headerElements = headerValue.Elements.ToDictionary(
            element => element.Key.Value?.ToString() ?? string.Empty,
            element => element.Value);

        Assert.Equal(
            SensitiveDataDestructuringPolicy.RedactedValue,
            Assert.IsType<ScalarValue>(headerElements[RefreshTokenHeaderName]).Value);
        Assert.NotEqual(
            SensitiveDataDestructuringPolicy.RedactedValue,
            Assert.IsType<ScalarValue>(headerElements[RefreshTokenExpiresAtHeaderName]).Value);
    }

    [Fact]
    public void InfrastructureLogging_ShouldConfigureMaskingAndFileSizeRolling()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "infrastructure",
            "IIoT.Infrastructure",
            "Logging",
            "SerilogExtensions.cs"));

        Assert.Contains(".Destructure.With<SensitiveDataDestructuringPolicy>()", source, StringComparison.Ordinal);
        Assert.Contains("fileSizeLimitBytes: SingleLogFileSizeLimitBytes", source, StringComparison.Ordinal);
        Assert.Contains("rollOnFileSizeLimit: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IIoTDbContext_ShouldNotContainLegacyFlushDomainEventsPlaceholder()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "infrastructure",
            "IIoT.EntityFrameworkCore",
            "IIoTDbContext.cs"));

        Assert.DoesNotContain("FlushDomainEventsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationBehavior_ShouldReturnInvalidGenericResult_AndSkipHandler()
    {
        var nextCalled = false;
        var behavior = new ValidationBehavior<TestValidationCommand, Result<bool>>(
            [new TestValidationCommandValidator()]);

        var result = await behavior.Handle(
            new TestValidationCommand(string.Empty),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success(true));
            },
            CancellationToken.None);

        Assert.False(nextCalled);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("Value is required.", result.Errors!);
    }

    [Fact]
    public async Task ValidationBehavior_ShouldReturnInvalidResult_AndSkipHandler()
    {
        var nextCalled = false;
        var behavior = new ValidationBehavior<TestValidationCommand, Result>(
            [new TestValidationCommandValidator()]);

        var result = await behavior.Handle(
            new TestValidationCommand(string.Empty),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success());
            },
            CancellationToken.None);

        Assert.False(nextCalled);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("Value is required.", result.Errors!);
    }

    [Fact]
    public void EventBusOptions_ShouldResolvePrefixedEndpointNameAndConsumerFallback()
    {
        var options = new EventBusOptions
        {
            ConcurrentMessageLimit = 6,
            EndpointPrefix = "prod"
        };

        Assert.Equal("prod-iiot-device-logs", options.ResolveEndpointName(RabbitMqEndpointNames.DeviceLogs));
        Assert.Equal(6, options.ResolveConcurrentMessageLimit());
        Assert.Equal(6, options.ResolveConcurrentMessageLimit(0));
        Assert.Equal(2, options.ResolveConcurrentMessageLimit(2));
    }

    [Fact]
    public void PostgresOptions_ShouldRejectInvalidRuntimeSettings()
    {
        var options = new PostgresOptions
        {
            CommandTimeoutSeconds = 0
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void OptionsBindingExtensions_ShouldThrowWhenRequiredSectionIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => configuration.GetRequiredValidatedOptions<PostgresOptions>(
            PostgresOptions.SectionName,
            static options => options.Validate());

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void OptionsBindingExtensions_ShouldBindValidateAndRegisterOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{EventBusOptions.SectionName}:EndpointPrefix"] = "prod"
        });

        var configured = builder.AddValidatedOptions<EventBusOptions>(
            EventBusOptions.SectionName,
            static options => options.Validate());

        using var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IOptions<EventBusOptions>>().Value;

        Assert.Equal("prod", configured.EndpointPrefix);
        Assert.Equal("prod", resolved.EndpointPrefix);
        Assert.Equal(4, resolved.ConcurrentMessageLimit);
    }

    private static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.Create(_ => { }).CreateLogger<T>();
    }

    private static object? GetScalarValue(StructureValue structureValue, string propertyName)
    {
        var property = Assert.Single(
            structureValue.Properties,
            property => string.Equals(property.Name, propertyName, StringComparison.Ordinal));
        return Assert.IsType<ScalarValue>(property.Value).Value;
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativeSegments[0]);
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return Path.Combine(current.FullName, Path.Combine(relativeSegments));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for source inspection.");
    }

    private sealed record TestValidationCommand(string Value);

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    private sealed class TestValidationCommandValidator : AbstractValidator<TestValidationCommand>
    {
        public TestValidationCommandValidator()
        {
            RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.");
        }
    }
}
