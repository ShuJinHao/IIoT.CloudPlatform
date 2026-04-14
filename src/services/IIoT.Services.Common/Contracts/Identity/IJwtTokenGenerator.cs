namespace IIoT.Services.Common.Contracts;

/// <summary>
/// JWT 令牌生成器契约。
/// </summary>
public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string employeeNo, IList<string> roles, IList<string> permissions);
}
