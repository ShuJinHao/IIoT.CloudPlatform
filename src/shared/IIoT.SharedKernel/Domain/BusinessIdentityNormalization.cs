namespace IIoT.SharedKernel.Domain;

/// <summary>
/// Cross-domain canonicalization for human-entered business classification
/// and display-name uniqueness keys. The canonical values are persisted so
/// application checks and database constraints use exactly the same rules.
/// </summary>
public static class BusinessIdentityNormalization
{
    public static string NormalizeClassificationCode(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = string.Concat(value.Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("编码不能只包含空白字符。", parameterName);
        }

        return normalized;
    }

    public static string NormalizeDisplayNameKey(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = string.Concat(value.Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("名称不能只包含空白字符。", parameterName);
        }

        return normalized;
    }
}
