namespace IIoT.Infrastructure.Authentication;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public const int MinimumSecretLength = 32;

    public string Secret { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; }
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;

    public void Validate()
    {
        if (ExpiryMinutes <= 0)
        {
            throw new InvalidOperationException("JwtSettings:ExpiryMinutes must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("JwtSettings:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JwtSettings:Audience is required.");
        }

        if (!string.IsNullOrWhiteSpace(Secret) && Secret.Trim().Length < MinimumSecretLength)
        {
            throw new InvalidOperationException(
                $"JwtSettings:Secret must be at least {MinimumSecretLength} characters when configured.");
        }
    }
}
