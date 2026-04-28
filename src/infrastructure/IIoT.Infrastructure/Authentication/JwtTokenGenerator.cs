using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IIoT.Infrastructure.Authentication;

public class JwtTokenGenerator(IOptions<JwtSettings> jwtOptions) : IJwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings = jwtOptions.Value;

    public JwtTokenResult GenerateHumanToken(
        Guid userId,
        string employeeNo,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, employeeNo),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, employeeNo),
            new(IIoTClaimTypes.ActorType, IIoTClaimTypes.HumanActor),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim(IIoTClaimTypes.Permission, permission));
        }

        return CreateToken(claims);
    }

    public JwtTokenResult GenerateEdgeDeviceToken(
        Guid deviceId,
        string clientCode,
        Guid processId)
    {
        var subject = $"device:{deviceId}";
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.UniqueName, clientCode),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, clientCode),
            new(IIoTClaimTypes.ActorType, IIoTClaimTypes.EdgeDeviceActor),
            new(IIoTClaimTypes.DeviceId, deviceId.ToString()),
            new(IIoTClaimTypes.ClientCode, clientCode),
            new(IIoTClaimTypes.ProcessId, processId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return CreateToken(claims);
    }

    private JwtTokenResult CreateToken(IReadOnlyCollection<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new JwtTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc);
    }
}
