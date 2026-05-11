using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.IdentityService.Queries;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class CloudOidcProviderBehaviorTests
{
    [Fact]
    public void OidcProviderOptions_Validate_ShouldRequireStableIssuerAndRedirectUri()
    {
        var valid = new OidcProviderOptions
        {
            Issuer = "https://cloud.example.com",
            AicopilotClientId = "aicopilot",
            AicopilotRedirectUris = ["https://ai.example.com/api/identity/cloud-oidc/callback"],
            AicopilotPostLogoutRedirectUris = ["https://ai.example.com/login"],
            AuthorizationCodeLifetimeMinutes = 3,
            AccessTokenLifetimeMinutes = 10,
            IdentityTokenLifetimeMinutes = 10,
            SessionIdleMinutes = 30,
            SessionCookieName = "__Host-IIoT-OidcSession"
        };

        valid.Validate();

        var invalid = new OidcProviderOptions
        {
            Issuer = "not-an-uri",
            AicopilotClientId = "aicopilot",
            AicopilotRedirectUris = []
        };

        Assert.Throws<InvalidOperationException>(() => invalid.Validate());
    }

    [Fact]
    public void CloudOidcUserProfile_ShouldKeepClaimsContractMinimal()
    {
        var properties = typeof(CloudOidcUserProfile)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var expectedProperties = new[]
            {
                nameof(CloudOidcUserProfile.UserId),
                nameof(CloudOidcUserProfile.EmployeeNo),
                nameof(CloudOidcUserProfile.RealName),
                nameof(CloudOidcUserProfile.AccountEnabled),
                nameof(CloudOidcUserProfile.EmployeeActive),
                nameof(CloudOidcUserProfile.TenantId),
                nameof(CloudOidcUserProfile.StatusVersion)
            }
            .OrderBy(property => property, StringComparer.Ordinal);

        Assert.Equal(expectedProperties, properties.OrderBy(property => property, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CloudOidcUserProfileService_ShouldReturnAccountAndEmployeeState()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var userId = Guid.NewGuid();
        var employee = new Employee(userId, "E-OIDC-001", "Cloud OIDC User");
        employee.Deactivate();

        dbContext.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "E-OIDC-001",
            IsEnabled = false
        });
        dbContext.Employees.Add(employee);
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
        var userId = Guid.NewGuid();
        var employee = new Employee(userId, "E-OIDC-STATUS", "Cloud Status User");

        dbContext.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "E-OIDC-STATUS",
            IsEnabled = true
        });
        dbContext.Employees.Add(employee);
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

    [Fact]
    public void CloudIdentityStatusQuery_ShouldRequireAiReadIdentityStatusPermission()
    {
        var attributes = typeof(GetCloudIdentityStatusQuery)
            .GetCustomAttributes(typeof(IIoT.Services.CrossCutting.Attributes.AuthorizeAiReadAttribute), inherit: true)
            .Cast<IIoT.Services.CrossCutting.Attributes.AuthorizeAiReadAttribute>()
            .ToArray();

        var attribute = Assert.Single(attributes);
        Assert.Equal(AiReadPermissions.IdentityStatus, attribute.Permission);
    }
}
