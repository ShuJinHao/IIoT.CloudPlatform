using System.Text.RegularExpressions;

namespace IIoT.Core.Production.Aggregates.Recipes.ValueObjects;

public readonly record struct RecipeVersion
{
    private static readonly Regex Pattern = new(
        @"^V\d+\.\d+$",
        RegexOptions.Compiled);

    private RecipeVersion(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static RecipeVersion Initial => new("V1.0");

    public static RecipeVersion From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalizedValue = value.Trim();

        if (!Pattern.IsMatch(normalizedValue))
        {
            throw new ArgumentException("Version 必须符合 Vx.y 格式。", nameof(value));
        }

        return new RecipeVersion(normalizedValue);
    }

    public override string ToString() => Value;
}
