using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using IIoT.Services.Contracts.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace IIoT.HttpApi.Infrastructure.Authentication;

internal static class AiIdentityStatusTokenValidator
{
    private static readonly long MaximumLifetimeSeconds =
        checked((long)TimeSpan.FromMinutes(
            AiIdentityStatusTokenDefaults.LifetimeMinutes).TotalSeconds);

    public static bool IsValid(SecurityToken securityToken)
    {
        var rawToken = securityToken switch
        {
            JwtSecurityToken jwt => jwt.RawData,
            JsonWebToken json => json.EncodedToken,
            _ => null
        };

        return IsValidRawToken(rawToken);
    }

    internal static bool IsValidRawToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var segments = rawToken.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                Base64UrlEncoder.DecodeBytes(segments[1]));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var claims = document.RootElement
                .EnumerateObject()
                .GroupBy(property => property.Name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(property => property.Value).ToArray(),
                    StringComparer.Ordinal);

            if (!TryGetUniqueString(
                    claims,
                    JwtRegisteredClaimNames.Iss,
                    out var issuer) ||
                !string.Equals(
                    issuer,
                    AiIdentityStatusTokenDefaults.DefaultIssuer,
                    StringComparison.Ordinal) ||
                !TryGetUniqueString(
                    claims,
                    JwtRegisteredClaimNames.Aud,
                    out var audience) ||
                !string.Equals(
                    audience,
                    AiIdentityStatusTokenDefaults.DefaultAudience,
                    StringComparison.Ordinal) ||
                !TryGetUniqueString(
                    claims,
                    AiIdentityStatusTokenDefaults.ActorClaimType,
                    out var actor) ||
                !string.Equals(
                    actor,
                    AiIdentityStatusTokenDefaults.Actor,
                    StringComparison.Ordinal) ||
                !TryGetUniqueString(
                    claims,
                    JwtRegisteredClaimNames.Sub,
                    out var subject) ||
                !string.Equals(
                    subject,
                    AiIdentityStatusTokenDefaults.Subject,
                    StringComparison.Ordinal) ||
                !TryGetUniqueInt64(claims, JwtRegisteredClaimNames.Iat, out var issuedAt) ||
                !TryGetUniqueInt64(claims, JwtRegisteredClaimNames.Nbf, out var notBefore) ||
                !TryGetUniqueInt64(claims, JwtRegisteredClaimNames.Exp, out var expiresAt))
            {
                return false;
            }

            return issuedAt >= 0 &&
                   notBefore == issuedAt &&
                   expiresAt > issuedAt &&
                   expiresAt - issuedAt <= MaximumLifetimeSeconds;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetUniqueString(
        IReadOnlyDictionary<string, JsonElement[]> claims,
        string claimType,
        out string value)
    {
        value = string.Empty;
        if (!claims.TryGetValue(claimType, out var values) ||
            values.Length != 1 ||
            values[0].ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = values[0].GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetUniqueInt64(
        IReadOnlyDictionary<string, JsonElement[]> claims,
        string claimType,
        out long value)
    {
        value = default;
        return claims.TryGetValue(claimType, out var values) &&
               values.Length == 1 &&
               values[0].ValueKind == JsonValueKind.Number &&
               values[0].TryGetInt64(out value);
    }
}
