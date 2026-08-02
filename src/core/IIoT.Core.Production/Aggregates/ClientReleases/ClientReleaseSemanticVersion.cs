namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 客户端发布域唯一的严格语义版本实现。
/// 只接受 MAJOR.MINOR.PATCH[-prerelease]，拒绝前导零、空预发布标识和 build metadata。
/// </summary>
public sealed class ClientReleaseSemanticVersion : IComparable<ClientReleaseSemanticVersion>
{
    private readonly string major;
    private readonly string minor;
    private readonly string patch;
    private readonly string[] prereleaseIdentifiers;

    private ClientReleaseSemanticVersion(
        string value,
        string major,
        string minor,
        string patch,
        string[] prereleaseIdentifiers)
    {
        Value = value;
        this.major = major;
        this.minor = minor;
        this.patch = patch;
        this.prereleaseIdentifiers = prereleaseIdentifiers;
    }

    public string Value { get; }

    public bool IsPrerelease => prereleaseIdentifiers.Length > 0;

    public static bool IsValid(string? value)
        => TryParse(value, out _);

    public static bool TryParse(
        string? value,
        out ClientReleaseSemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('+', StringComparison.Ordinal))
        {
            return false;
        }

        var prereleaseSeparator = value.IndexOf('-');
        var core = prereleaseSeparator < 0
            ? value
            : value[..prereleaseSeparator];
        var coreIdentifiers = core.Split('.', StringSplitOptions.None);
        if (coreIdentifiers.Length != 3
            || coreIdentifiers.Any(identifier => !IsValidNumericIdentifier(identifier)))
        {
            return false;
        }

        string[] prereleaseIdentifiers = [];
        if (prereleaseSeparator >= 0)
        {
            var prerelease = value[(prereleaseSeparator + 1)..];
            prereleaseIdentifiers = prerelease.Split('.', StringSplitOptions.None);
            if (prereleaseIdentifiers.Length == 0
                || prereleaseIdentifiers.Any(identifier =>
                    !IsValidPrereleaseIdentifier(identifier)))
            {
                return false;
            }
        }

        version = new ClientReleaseSemanticVersion(
            value,
            coreIdentifiers[0],
            coreIdentifiers[1],
            coreIdentifiers[2],
            prereleaseIdentifiers);
        return true;
    }

    public static ClientReleaseSemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var parsed))
        {
            throw new FormatException(
                "客户端发布版本必须符合 MAJOR.MINOR.PATCH[-prerelease]。");
        }

        return parsed!;
    }

    public static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!TryParse(value, out _))
        {
            throw new ArgumentException(
                "客户端发布版本必须符合 MAJOR.MINOR.PATCH[-prerelease]。",
                parameterName);
        }

        return value;
    }

    public static int Compare(string left, string right)
        => Parse(left).CompareTo(Parse(right));

    public static bool IsInRange(
        string version,
        string minimum,
        string maximum)
    {
        var parsed = Parse(version);
        return parsed.CompareTo(Parse(minimum)) >= 0
               && parsed.CompareTo(Parse(maximum)) <= 0;
    }

    public int CompareTo(ClientReleaseSemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison = CompareNumericIdentifier(major, other.major);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        coreComparison = CompareNumericIdentifier(minor, other.minor);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        coreComparison = CompareNumericIdentifier(patch, other.patch);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (!IsPrerelease && !other.IsPrerelease)
        {
            return 0;
        }

        if (!IsPrerelease)
        {
            return 1;
        }

        if (!other.IsPrerelease)
        {
            return -1;
        }

        var count = Math.Min(
            prereleaseIdentifiers.Length,
            other.prereleaseIdentifiers.Length);
        for (var index = 0; index < count; index++)
        {
            var left = prereleaseIdentifiers[index];
            var right = other.prereleaseIdentifiers[index];
            var leftNumeric = IsAsciiDigits(left);
            var rightNumeric = IsAsciiDigits(right);
            int comparison;
            if (leftNumeric && rightNumeric)
            {
                comparison = CompareNumericIdentifier(left, right);
            }
            else if (leftNumeric)
            {
                comparison = -1;
            }
            else if (rightNumeric)
            {
                comparison = 1;
            }
            else
            {
                comparison = string.Compare(left, right, StringComparison.Ordinal);
            }

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return prereleaseIdentifiers.Length.CompareTo(
            other.prereleaseIdentifiers.Length);
    }

    public override string ToString() => Value;

    private static bool IsValidNumericIdentifier(string value)
        => value.Length > 0
           && IsAsciiDigits(value)
           && (value.Length == 1 || value[0] != '0');

    private static bool IsValidPrereleaseIdentifier(string value)
    {
        if (value.Length == 0
            || value.Any(character =>
                !IsAsciiAlphaNumeric(character) && character != '-'))
        {
            return false;
        }

        return !IsAsciiDigits(value)
               || value.Length == 1
               || value[0] != '0';
    }

    private static bool IsAsciiDigits(string value)
        => value.Length > 0
           && value.All(character => character is >= '0' and <= '9');

    private static bool IsAsciiAlphaNumeric(char value)
        => value is >= '0' and <= '9'
           or >= 'A' and <= 'Z'
           or >= 'a' and <= 'z';

    private static int CompareNumericIdentifier(string left, string right)
    {
        var lengthComparison = left.Length.CompareTo(right.Length);
        return lengthComparison != 0
            ? lengthComparison
            : string.Compare(left, right, StringComparison.Ordinal);
    }
}
