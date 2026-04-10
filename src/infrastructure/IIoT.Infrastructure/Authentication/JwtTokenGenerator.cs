using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IIoT.Services.Common.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IIoT.Infrastructure.Authentication;

public class JwtTokenGenerator(IOptions<JwtSettings> jwtOptions) : IJwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings = jwtOptions.Value;

    public string GenerateToken(Guid userId, string employeeNo, IList<string> roles, IList<string> permissions)
    {
        // 1. 定义标准的 JWT 声明 (Claims)
        var claims = new List<Claim>
        {
            // sub (Subject): 存放用户的灵魂绑定 Guid
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),

            // unique_name: 存放用户的工号
            new Claim(JwtRegisteredClaimNames.UniqueName, employeeNo),

            // jti (JWT ID): 生成一个唯一的 Token ID 防止重放
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // 2. 将角色循环加入声明中
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // 3. 将权限点加入 claims(前端从 Token 解析 "Permission" claim)
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("Permission", permission));
        }

        // 4. 读取密钥并选择加密算法
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 5. 组装 Token
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
            signingCredentials: credentials);

        // 6. 序列化为前端可读的字符串
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
