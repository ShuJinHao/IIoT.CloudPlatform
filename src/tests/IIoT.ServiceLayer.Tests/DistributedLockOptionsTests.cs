using IIoT.Infrastructure;
using IIoT.Infrastructure.Locking;
using IIoT.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class DistributedLockOptionsTests
{
    [Theory]
    [InlineData(4, 1, 100, 100)]
    [InlineData(5, 5, 100, 100)]
    [InlineData(5, 1, 0, 100)]
    [InlineData(5, 1, 100, 0)]
    public void Options_ShouldRejectUnsafeLeaseTiming(
        int leaseSeconds,
        int renewalCadenceSeconds,
        int operationTimeoutMilliseconds,
        int renewalShutdownTimeoutMilliseconds)
    {
        var options = new DistributedLockOptions
        {
            LeaseSeconds = leaseSeconds,
            RenewalCadenceSeconds = renewalCadenceSeconds,
            OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            RenewalShutdownTimeoutMilliseconds = renewalShutdownTimeoutMilliseconds
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Options_ShouldReserveTwoOperationTimeoutsBeforeLeaseExpiry()
    {
        var options = new DistributedLockOptions
        {
            LeaseSeconds = 120,
            RenewalCadenceSeconds = 100,
            OperationTimeoutMilliseconds = 10_000,
            RenewalShutdownTimeoutMilliseconds = 5_000
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task InfrastructureRegistration_ShouldRejectUnsafeTimingOnStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:redis-cache"] = "127.0.0.1:1,abortConnect=false,connectTimeout=50",
            ["DistributedLock:LeaseSeconds"] = "120",
            ["DistributedLock:RenewalCadenceSeconds"] = "100",
            ["DistributedLock:OperationTimeoutMilliseconds"] = "10000",
            ["DistributedLock:RenewalShutdownTimeoutMilliseconds"] = "5000"
        });
        builder.AddInfrastructures();
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void LockContract_ShouldExposeOnlyTheOwnershipAwareLeaseReturnType()
    {
        var acquireMethod = Assert.Single(typeof(IDistributedLockService).GetMethods());

        Assert.Equal(
            typeof(Task<IDistributedLockLease>),
            acquireMethod.ReturnType);
    }
}
