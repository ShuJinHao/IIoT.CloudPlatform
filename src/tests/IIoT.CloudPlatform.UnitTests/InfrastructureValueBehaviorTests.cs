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

namespace IIoT.CloudPlatform.UnitTests;

public sealed class InfrastructureValueBehaviorTests
{
    private const string RefreshTokenHeaderName = "X-IIoT-Refresh-Token";
    private const string RefreshTokenExpiresAtHeaderName = "X-IIoT-Refresh-Token-Expires-At";

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
    public void JwtSettings_ShouldRejectConfiguredSecretShorterThanRuntimeMinimum()
    {
        var options = new JwtSettings
        {
            Secret = "short-jwt-secret",
            ExpiryMinutes = 60,
            Issuer = "IIoT.CloudPlatform",
            Audience = "IIoT.EdgeClient"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("JwtSettings:Secret must be at least 32 characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JwtSettings_ShouldAllowDevelopmentSecretResolverToHandleMissingSecret()
    {
        var options = new JwtSettings
        {
            Secret = string.Empty,
            ExpiryMinutes = 60,
            Issuer = "IIoT.CloudPlatform",
            Audience = "IIoT.EdgeClient"
        };

        options.Validate();
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

    private static object? GetScalarValue(StructureValue structureValue, string propertyName)
    {
        var property = Assert.Single(
            structureValue.Properties,
            property => string.Equals(property.Name, propertyName, StringComparison.Ordinal));
        return Assert.IsType<ScalarValue>(property.Value).Value;
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
