namespace IIoT.Services.CrossCutting.Caching.Options;

public class PermissionCacheOptions
{
    public const string SectionName = "PermissionCache";

    public string KeyPrefix { get; set; } = "iiot:permissions:v1:";

    public int ExpirationMinutes { get; set; }

    public int ExpirationHours { get; set; }

    public TimeSpan ResolveExpiration()
    {
        if (ExpirationMinutes > 0)
        {
            return TimeSpan.FromMinutes(ExpirationMinutes);
        }

        if (ExpirationHours > 0)
        {
            return TimeSpan.FromHours(ExpirationHours);
        }

        return TimeSpan.FromMinutes(10);
    }
}
