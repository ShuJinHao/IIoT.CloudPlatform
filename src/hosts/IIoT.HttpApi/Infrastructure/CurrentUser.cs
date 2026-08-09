using System.Security.Claims;
using IIoT.Services.Contracts.Identity;

namespace IIoT.HttpApi.Infrastructure;

public sealed class CurrentUser : ICurrentUser
{
    public string? Id { get; }
    public string? UserName { get; }
    public IReadOnlyCollection<string> Roles { get; } = [];
    public string? ActorType { get; }
    public IReadOnlyCollection<string> Permissions { get; } = [];
    public Guid? DeviceId { get; }
    public string? ClientCode { get; }
    public Guid? InstallerGenerationId { get; }
    public bool IsAuthenticated { get; }

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        UserName = user.FindFirstValue(ClaimTypes.Name);
        Roles = user.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ActorType = user.FindFirstValue(IIoTClaimTypes.ActorType);
        Permissions = user.FindAll(IIoTClaimTypes.Permission)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (Guid.TryParse(user.FindFirstValue(IIoTClaimTypes.DeviceId), out var deviceId))
        {
            DeviceId = deviceId;
        }

        ClientCode = user.FindFirstValue(IIoTClaimTypes.ClientCode)?.Trim().ToUpperInvariant();

        if (Guid.TryParse(
                user.FindFirstValue(IIoTClaimTypes.InstallerGenerationId),
                out var generationId))
        {
            InstallerGenerationId = generationId;
        }

        IsAuthenticated = true;
    }
}
