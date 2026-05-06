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

    public Guid? DelegatedUserId
    {
        get
        {
            var raw = User?.FindFirstValue(IIoTClaimTypes.DelegatedUserId);
            return Guid.TryParse(raw, out var userId) ? userId : null;
        }
    }

    public IReadOnlyCollection<Guid>? DelegatedDeviceIds
    {
        get
        {
            var deviceIds = User?
                .FindAll(IIoTClaimTypes.DelegatedDeviceId)
                .Select(claim => Guid.TryParse(claim.Value, out var deviceId) ? deviceId : (Guid?)null)
                .Where(deviceId => deviceId.HasValue)
                .Select(deviceId => deviceId!.Value)
                .Distinct()
                .ToArray();

            return deviceIds is { Length: > 0 } ? deviceIds : null;
        }
    }

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;
}
