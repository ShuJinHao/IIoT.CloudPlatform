using IIoT.IdentityService.Queries;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Xunit;

namespace IIoT.CloudPlatform.ContractTests;

public sealed class CloudOidcProviderBehaviorTests
{
    [Fact]
    public void OidcProviderOptions_Validate_ShouldRequireStableIssuerAndRedirectUri()
    {
        var valid = CreateValidOidcProviderOptions();

        valid.Validate();

        var invalid = new OidcProviderOptions
        {
            Issuer = "not-an-uri",
            AicopilotClientId = "aicopilot",
            AicopilotRedirectUris = []
        };

        Assert.Throws<InvalidOperationException>(() => invalid.Validate());
    }

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:5178/api/identity/cloud-oidc/callback", "http://localhost:5178/login")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:5178/api/identity/cloud-oidc/callback", "http://127.0.0.1:5178/login")]
    [InlineData("http://[::1]:8080", "http://[::1]:5178/api/identity/cloud-oidc/callback", "http://[::1]:5178/login")]
    public void OidcProviderOptions_Validate_ShouldAllowDevelopmentLoopbackHttp(
        string issuer,
        string redirectUri,
        string postLogoutRedirectUri)
    {
        var options = CreateValidOidcProviderOptions();
        options.Issuer = issuer;
        options.AicopilotRedirectUris = [redirectUri];
        options.AicopilotPostLogoutRedirectUris = [postLogoutRedirectUri];

        options.Validate("Development");
    }

    [Theory]
    [InlineData("Production", "http://localhost:8080", "https://ai.example.com/api/identity/cloud-oidc/callback", "https://ai.example.com/login")]
    [InlineData("Production", "https://cloud.example.com", "http://127.0.0.1:5178/api/identity/cloud-oidc/callback", "https://ai.example.com/login")]
    [InlineData("Production", "https://cloud.example.com", "https://ai.example.com/api/identity/cloud-oidc/callback", "http://localhost:5178/login")]
    [InlineData("Development", "http://cloud.example.com", "http://127.0.0.1:5178/api/identity/cloud-oidc/callback", "http://127.0.0.1:5178/login")]
    public void OidcProviderOptions_Validate_ShouldRejectInsecureOidcUris(
        string environmentName,
        string issuer,
        string redirectUri,
        string postLogoutRedirectUri)
    {
        var options = CreateValidOidcProviderOptions();
        options.Issuer = issuer;
        options.AicopilotRedirectUris = [redirectUri];
        options.AicopilotPostLogoutRedirectUris = [postLogoutRedirectUri];

        Assert.Throws<InvalidOperationException>(() => options.Validate(environmentName));
    }

    [Theory]
    [InlineData("http://10.98.90.154:81", "http://10.98.90.154:82/api/identity/cloud-oidc/callback", "http://10.98.90.154:82/login")]
    [InlineData("http://192.168.1.10:81", "http://192.168.1.11:82/api/identity/cloud-oidc/callback", "http://192.168.1.11:82/login")]
    [InlineData("http://172.16.0.10:81", "http://172.31.255.11:82/api/identity/cloud-oidc/callback", "http://172.31.255.11:82/login")]
    [InlineData("http://localhost:8080", "http://localhost:5178/api/identity/cloud-oidc/callback", "http://localhost:5178/login")]
    public void OidcProviderOptions_Validate_ShouldAllowExplicitIntranetHttpOidc(
        string issuer,
        string redirectUri,
        string postLogoutRedirectUri)
    {
        var options = CreateValidOidcProviderOptions();
        options.Issuer = issuer;
        options.AllowIntranetHttpOidc = true;
        options.AicopilotRedirectUris = [redirectUri];
        options.AicopilotPostLogoutRedirectUris = [postLogoutRedirectUri];

        options.Validate("Production");

        Assert.Equal("IIoT-OidcSession", options.GetEffectiveSessionCookieName());
    }

    [Theory]
    [InlineData("http://cloud.example.com", "http://10.98.90.154:82/api/identity/cloud-oidc/callback", "http://10.98.90.154:82/login")]
    [InlineData("http://10.98.90.154:81", "http://ai.example.com/api/identity/cloud-oidc/callback", "http://10.98.90.154:82/login")]
    [InlineData("http://10.98.90.154:81", "http://10.98.90.154:82/api/identity/cloud-oidc/callback", "http://ai.example.com/login")]
    [InlineData("http://8.8.8.8:81", "http://10.98.90.154:82/api/identity/cloud-oidc/callback", "http://10.98.90.154:82/login")]
    [InlineData("http://10.98.90.154:81", "http://1.1.1.1:82/api/identity/cloud-oidc/callback", "http://10.98.90.154:82/login")]
    [InlineData("http://10.98.90.154:81", "http://10.98.90.154:82/api/identity/cloud-oidc/callback", "http://203.0.113.10:82/login")]
    public void OidcProviderOptions_Validate_ShouldRejectPublicHttpEvenWhenIntranetHttpOidcIsEnabled(
        string issuer,
        string redirectUri,
        string postLogoutRedirectUri)
    {
        var options = CreateValidOidcProviderOptions();
        options.Issuer = issuer;
        options.AllowIntranetHttpOidc = true;
        options.AicopilotRedirectUris = [redirectUri];
        options.AicopilotPostLogoutRedirectUris = [postLogoutRedirectUri];

        Assert.Throws<InvalidOperationException>(() => options.Validate("Production"));
    }

    [Fact]
    public void OidcProviderOptions_GetEffectiveSessionCookieName_ShouldKeepHostPrefixWhenHttpsMode()
    {
        var options = CreateValidOidcProviderOptions();

        Assert.Equal("__Host-IIoT-OidcSession", options.GetEffectiveSessionCookieName());
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
    public void CloudIdentityStatusQuery_ShouldRequireAiReadIdentityStatusPermission()
    {
        var attributes = typeof(GetCloudIdentityStatusQuery)
            .GetCustomAttributes(typeof(IIoT.Services.CrossCutting.Attributes.AuthorizeAiReadAttribute), inherit: true)
            .Cast<IIoT.Services.CrossCutting.Attributes.AuthorizeAiReadAttribute>()
            .ToArray();

        var attribute = Assert.Single(attributes);
        Assert.Equal(AiReadPermissions.IdentityStatus, attribute.Permission);
    }

    private static OidcProviderOptions CreateValidOidcProviderOptions()
    {
        return new OidcProviderOptions
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
    }
}
