namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseText
{
    public static string NormalizeChannel(string? value)
        => string.IsNullOrWhiteSpace(value) ? "stable" : value.Trim();

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
