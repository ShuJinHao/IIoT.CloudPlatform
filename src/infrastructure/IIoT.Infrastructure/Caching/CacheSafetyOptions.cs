namespace IIoT.Infrastructure.Caching;

/// <summary>
/// FusionCache 安全相关配置。
/// </summary>
public sealed class CacheSafetyOptions
{
    public const string SectionName = "CacheSafety";

    public int FailSafeMinutes { get; set; } = 30;

    public TimeSpan ResolveFailSafeDuration()
    {
        return TimeSpan.FromMinutes(Math.Max(FailSafeMinutes, 1));
    }
}
