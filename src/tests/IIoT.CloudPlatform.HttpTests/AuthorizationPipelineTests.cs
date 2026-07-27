using IIoT.HttpApi;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.DependencyInjection;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class AuthorizationPipelineTests
{
    [Fact]
    public void AdminOnlyGuard_ShouldRunBeforePermissionAuthorizationAndDistributedLock()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddConfiguredMediatR(
            configuration,
            DependencyInjection.ConfigureApplicationMediatR);

        var behaviorTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        var adminOnlyIndex = behaviorTypes.IndexOf(typeof(AdminOnlyBehavior<,>));
        var authorizationIndex = behaviorTypes.IndexOf(typeof(AuthorizationBehavior<,>));
        var distributedLockIndex = behaviorTypes.IndexOf(typeof(DistributedLockBehavior<,>));

        Assert.True(adminOnlyIndex >= 0);
        Assert.True(authorizationIndex >= 0);
        Assert.True(distributedLockIndex >= 0);
        Assert.True(adminOnlyIndex < authorizationIndex);
        Assert.True(authorizationIndex < distributedLockIndex);
    }
}
