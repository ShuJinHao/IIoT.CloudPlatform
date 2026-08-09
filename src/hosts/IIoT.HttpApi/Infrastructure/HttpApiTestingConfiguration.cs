using Microsoft.Extensions.Configuration;

namespace IIoT.HttpApi.Infrastructure;

public static class HttpApiTestingConfiguration
{
    public const string IdentityOnlyHostConfigurationKey =
        "HttpApi:Testing:IdentityOnlyHost";

    public static void ApplyIdentityOnlyPassStationType(
        ConfigurationManager configuration,
        string? environmentName)
    {
        var enabled = configuration.GetValue<bool>(
            IdentityOnlyHostConfigurationKey);
        EnsureAllowed(enabled, environmentName);
        if (!enabled)
        {
            return;
        }

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PassStationTypes:Types:0:TypeKey"] = "identityTest"
        });
    }

    public static void EnsureAllowed(bool enabled, string? environmentName)
    {
        if (enabled &&
            !string.Equals(environmentName, "Testing", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{IdentityOnlyHostConfigurationKey} is restricted to the Testing environment.");
        }
    }
}
