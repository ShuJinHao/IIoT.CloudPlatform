namespace IIoT.HttpApi.Infrastructure;

public static class HttpApiPolicies
{
    public const string RequireHumanUserToken = nameof(RequireHumanUserToken);
    public const string RequireEdgeDeviceToken = nameof(RequireEdgeDeviceToken);
    public const string RequireEdgeActivationToken = nameof(RequireEdgeActivationToken);
    public const string RequireAiReadDelegation = nameof(RequireAiReadDelegation);
    public const string RequireAiIdentityStatusToken = nameof(RequireAiIdentityStatusToken);
}
