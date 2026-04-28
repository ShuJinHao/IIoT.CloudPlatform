using FluentAssertions;
using IIoT.HttpApi.Infrastructure;
using IIoT.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IIoT.EndToEndTests;

public sealed class InternalHealthProbeBehaviorTests
{
    [Fact]
    public async Task PostgresReadinessHealthCheck_ShouldReturnUnhealthy_WhenDatabaseConnectionFails()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConnectionResourceNames.IiotDatabase}"] =
                    "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=iiot-db;Timeout=1;Command Timeout=1;Pooling=false"
            })
            .Build();
        var healthCheck = new PostgresReadinessHealthCheck(configuration);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), timeout.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
