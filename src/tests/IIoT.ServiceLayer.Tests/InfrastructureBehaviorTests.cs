using FluentValidation;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EventBus;
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
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class InfrastructureBehaviorTests
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

        Assert.Equal(1, dispatched);
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

        await dispatcher.DispatchPendingAsync();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync();

        Assert.Null(outboxMessage.ProcessedAtUtc);
        Assert.Equal(1, outboxMessage.AttemptCount);
        Assert.Equal("dispatch failed", outboxMessage.LastError);
        Assert.NotNull(outboxMessage.LastAttemptedAtUtc);
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

    private sealed record TestValidationCommand(string Value);

    private sealed class TestValidationCommandValidator : AbstractValidator<TestValidationCommand>
    {
        public TestValidationCommandValidator()
        {
            RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.");
        }
    }
}
