using System.Security.Claims;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;

namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpAiReadScopeAccessor(IHttpContextAccessor httpContextAccessor) : IAiReadScopeAccessor
{
    public string Caller =>
        User?.FindFirstValue(ClaimTypes.Name)
        ?? User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown-ai-service-account";

    public AiReadScopeKind ScopeKind => Scope.Kind;

    public Guid? DelegatedUserId => Scope.DelegatedUserId;

    public IReadOnlyCollection<Guid>? DelegatedDeviceIds => Scope.DelegatedDeviceIds;

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    private ParsedAiReadScope Scope => ParseScope(User);

    private static ParsedAiReadScope ParseScope(ClaimsPrincipal? user)
    {
        var delegatedUserClaims = user?
            .FindAll(IIoTClaimTypes.DelegatedUserId)
            .Select(claim => claim.Value)
            .ToArray() ?? [];
        var delegatedDeviceClaims = user?
            .FindAll(IIoTClaimTypes.DelegatedDeviceId)
            .Select(claim => claim.Value)
            .ToArray() ?? [];

        if (delegatedUserClaims.Length == 0 && delegatedDeviceClaims.Length == 0)
        {
            return new ParsedAiReadScope(AiReadScopeKind.Global, null, null);
        }

        if (delegatedUserClaims.Length != 1
            || !Guid.TryParse(delegatedUserClaims[0], out var delegatedUserId)
            || delegatedUserId == Guid.Empty)
        {
            return InvalidScope();
        }

        var delegatedDeviceIds = new List<Guid>(delegatedDeviceClaims.Length);
        foreach (var rawDeviceId in delegatedDeviceClaims)
        {
            if (!Guid.TryParse(rawDeviceId, out var delegatedDeviceId)
                || delegatedDeviceId == Guid.Empty)
            {
                return InvalidScope();
            }

            delegatedDeviceIds.Add(delegatedDeviceId);
        }

        return new ParsedAiReadScope(
            AiReadScopeKind.Delegated,
            delegatedUserId,
            delegatedDeviceIds.Distinct().ToArray());
    }

    private static ParsedAiReadScope InvalidScope()
        => new(AiReadScopeKind.Invalid, null, []);

    private sealed record ParsedAiReadScope(
        AiReadScopeKind Kind,
        Guid? DelegatedUserId,
        IReadOnlyCollection<Guid>? DelegatedDeviceIds);
}
