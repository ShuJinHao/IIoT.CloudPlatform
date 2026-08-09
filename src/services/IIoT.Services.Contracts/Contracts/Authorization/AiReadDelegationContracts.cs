using System.Text;

namespace IIoT.Services.Contracts.Authorization;

public static class AiReadDelegationDefaults
{
    public const string Scope = "iiot.ai.read";
    public const string Audience = "iiot-cloud-ai-read";
    public const string Actor = "ai-delegated-user";
    public const int LifetimeMinutes = 30;
}

public static class AiIdentityStatusTokenDefaults
{
    public const string AuthenticationScheme = "AiIdentityStatusBearer";
    public const string Actor = "ai-identity-status-system";
    public const string ActorClaimType = "actor_type";
    public const string Subject = "aicopilot-identity-status";
    public const string DefaultIssuer = "iiot-cloud-system";
    public const string DefaultAudience = "iiot-cloud-identity-status";
    public const int LifetimeMinutes = 5;
}

public sealed class AiIdentityStatusTokenOptions
{
    public const string SectionName = "AiIdentityStatusToken";

    public bool Enabled { get; set; }

    public string Issuer { get; set; } = AiIdentityStatusTokenDefaults.DefaultIssuer;

    public string Audience { get; set; } = AiIdentityStatusTokenDefaults.DefaultAudience;

    public string SigningSecret { get; set; } = string.Empty;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (!string.Equals(
                Issuer,
                AiIdentityStatusTokenDefaults.DefaultIssuer,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"AiIdentityStatusToken:Issuer must equal '{AiIdentityStatusTokenDefaults.DefaultIssuer}'.");
        }

        if (!string.Equals(
                Audience,
                AiIdentityStatusTokenDefaults.DefaultAudience,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"AiIdentityStatusToken:Audience must equal '{AiIdentityStatusTokenDefaults.DefaultAudience}'.");
        }

        if (string.IsNullOrWhiteSpace(SigningSecret) ||
            Encoding.UTF8.GetByteCount(SigningSecret) < 32)
        {
            throw new InvalidOperationException(
                "AiIdentityStatusToken:SigningSecret must contain at least 32 UTF-8 bytes.");
        }
    }
}

public sealed record AiReadDelegatedAuthorization(
    Guid CloudUserId,
    bool IsAdministrator,
    IReadOnlyCollection<Guid>? AllowedDeviceIds);

public interface IAiReadDelegatedAuthorizationService
{
    Task<AiReadDelegatedAuthorization?> AuthorizeAsync(
        Guid cloudUserId,
        string issuedStatusVersion,
        IReadOnlyCollection<string> requiredPermissions,
        CancellationToken cancellationToken = default);
}

public interface IAiReadAuthorizationContext
{
    bool IsInitialized { get; }

    void Initialize(AiReadDelegatedAuthorization authorization);
}
