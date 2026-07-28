using System.Security.Claims;
using IIoT.HttpApi;
using IIoT.HttpApi.Infrastructure;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.Services.Contracts.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
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

    [Fact]
    public async Task HumanUserPolicy_ShouldAllowAuthenticatedHumanActor()
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(IIoTClaimTypes.HumanActor);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            HttpApiPolicies.RequireHumanUserToken);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(IIoTClaimTypes.EdgeDeviceActor)]
    [InlineData(IIoTClaimTypes.AiServiceActor)]
    [InlineData(IIoTClaimTypes.EdgeReleasePublisherActor)]
    public async Task HumanUserPolicy_ShouldRejectAuthenticatedMachineActors(string actorType)
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(actorType);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            HttpApiPolicies.RequireHumanUserToken);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HumanUserPolicy_ShouldRejectUnauthenticatedCaller()
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();

        var result = await authorizationService.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null,
            HttpApiPolicies.RequireHumanUserToken);

        Assert.False(result.Succeeded);
    }

    private static ServiceProvider CreateAuthorizationServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var authenticatedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        DependencyInjection.ConfigureHttpAuthorization(
            services.AddAuthorizationBuilder(),
            authenticatedUserPolicy);

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(string actorType)
    {
        var identity = new ClaimsIdentity(
            [new Claim(IIoTClaimTypes.ActorType, actorType)],
            authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }
}
