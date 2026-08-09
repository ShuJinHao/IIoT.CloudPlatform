using IIoT.Services.Contracts.Authorization;

namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpAiReadScopeAccessor : IAiReadScopeAccessor, IAiReadAuthorizationContext
{
    private AiReadDelegatedAuthorization? authorization;

    public bool IsInitialized => authorization is not null;

    public string Caller => authorization?.CloudUserId.ToString("D") ?? "unverified-ai-delegation";

    public AiReadScopeKind ScopeKind => authorization switch
    {
        null => AiReadScopeKind.Invalid,
        { IsAdministrator: true } => AiReadScopeKind.Global,
        _ => AiReadScopeKind.Delegated
    };

    public Guid? DelegatedUserId => authorization?.CloudUserId;

    public IReadOnlyCollection<Guid>? DelegatedDeviceIds => authorization?.AllowedDeviceIds;

    public void Initialize(AiReadDelegatedAuthorization effectiveAuthorization)
    {
        ArgumentNullException.ThrowIfNull(effectiveAuthorization);
        if (authorization is not null)
        {
            throw new InvalidOperationException(
                "AiRead authorization context has already been initialized for this request.");
        }

        authorization = effectiveAuthorization;
    }
}
