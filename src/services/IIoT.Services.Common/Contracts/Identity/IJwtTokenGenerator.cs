namespace IIoT.Services.Common.Contracts.Identity;

public interface IJwtTokenGenerator
{
    string GenerateToken(
        Guid userId,
        string userName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        Guid? deviceId = null);
}
