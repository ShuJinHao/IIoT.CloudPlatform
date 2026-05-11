using IIoT.Services.Contracts.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class OpenIddictClientSeeder(
    IOpenIddictApplicationManager applicationManager,
    IOptions<OidcProviderOptions> options) : IOidcClientSeeder
{
    public async Task EnsureAicopilotClientAsync(CancellationToken cancellationToken = default)
    {
        var oidcOptions = options.Value;
        var descriptor = CreateDescriptor(oidcOptions);
        var existing = await applicationManager.FindByClientIdAsync(
            oidcOptions.AicopilotClientId,
            cancellationToken);

        if (existing is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
            return;
        }

        await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(OidcProviderOptions options)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.AicopilotClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "AICopilot",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Profile
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };

        foreach (var redirectUri in options.AicopilotRedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri));
        }

        foreach (var postLogoutRedirectUri in options.AicopilotPostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutRedirectUri));
        }

        return descriptor;
    }
}
