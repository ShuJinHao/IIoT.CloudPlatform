using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.IdentityService.Queries;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class CloudOidcPersistenceTests
{
    [Fact]
    public async Task CloudOidcUserProfileService_ShouldReturnAccountAndEmployeeState()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-OIDC-001",
            "Cloud OIDC User",
            accountEnabled: false,
            employeeActive: false);
        var userId = employee.Id;
        await dbContext.SaveChangesAsync();

        var service = new CloudOidcUserProfileService(dbContext);

        var profile = await service.GetByEmployeeNoAsync("E-OIDC-001");

        Assert.NotNull(profile);
        Assert.Equal(userId, profile.UserId);
        Assert.Equal("E-OIDC-001", profile.EmployeeNo);
        Assert.Equal("Cloud OIDC User", profile.RealName);
        Assert.False(profile.AccountEnabled);
        Assert.False(profile.EmployeeActive);
        Assert.Null(profile.TenantId);
        Assert.Equal(
            CloudIdentityStatusVersions.Create(userId, accountEnabled: false, employeeActive: false, employee.RowVersion),
            profile.StatusVersion);
    }

    [Fact]
    public async Task CloudIdentityStatusHandler_ShouldReturnDeterministicStatusVersion()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-OIDC-STATUS",
            "Cloud Status User");
        var userId = employee.Id;
        await dbContext.SaveChangesAsync();

        var service = new CloudOidcUserProfileService(dbContext);
        var handler = new GetCloudIdentityStatusHandler(service);

        var result = await handler.Handle(
            new GetCloudIdentityStatusQuery(userId, CloudIdentityTenants.Default),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value.CloudUserId);
        Assert.Equal(CloudIdentityTenants.Default, result.Value.TenantId);
        Assert.True(result.Value.AccountEnabled);
        Assert.True(result.Value.EmployeeActive);
        Assert.Equal(
            CloudIdentityStatusVersions.Create(userId, accountEnabled: true, employeeActive: true, employee.RowVersion),
            result.Value.StatusVersion);
    }
}
