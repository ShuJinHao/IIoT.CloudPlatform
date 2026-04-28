namespace IIoT.Services.Contracts.Identity;

public sealed record JwtTokenResult(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public interface IJwtTokenGenerator
{
    JwtTokenResult GenerateHumanToken(
        Guid userId,
        string userName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions);

    JwtTokenResult GenerateEdgeDeviceToken(
        Guid deviceId,
        string clientCode,
        Guid processId);
}
