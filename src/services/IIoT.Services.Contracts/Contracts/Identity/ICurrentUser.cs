namespace IIoT.Services.Contracts.Identity;

/// <summary>
/// 当前登录用户抽象。
/// 由宿主从 JWT 和 HttpContext 中解析后提供给应用层使用。
/// </summary>
public interface ICurrentUser
{
    string? Id { get; }

    string? UserName { get; }

    string? Role { get; }

    Guid? DeviceId { get; }

    bool IsAuthenticated { get; }
}
