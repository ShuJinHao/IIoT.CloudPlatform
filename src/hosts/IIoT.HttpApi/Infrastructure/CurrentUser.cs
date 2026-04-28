using System.Security.Claims;
using IIoT.Services.Contracts.Identity;

namespace IIoT.HttpApi.Infrastructure;

public sealed class CurrentUser : ICurrentUser
{
    public string? Id { get; }
    public string? UserName { get; }
    public string? Role { get; }
    public Guid? DeviceId { get; }
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
        Role = user.FindFirstValue(ClaimTypes.Role);

        if (Guid.TryParse(user.FindFirstValue(IIoTClaimTypes.DeviceId), out var deviceId))
        {
            DeviceId = deviceId;
        }

        IsAuthenticated = true;
    }
}
