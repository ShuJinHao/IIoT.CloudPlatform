using System.Text.Json;

namespace IIoT.ProductionService.Validators;

public sealed class RecipeParametersJsonbValidator
{
    public IReadOnlyList<string> Validate(string? rawJson)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errors.Add("Recipe parameters are required.");
            return errors;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            errors.Add($"Recipe parameter JSON is invalid: {ex.Message}");
            return errors;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Recipe parameters must be an array.");
                return errors;
            }

            var items = document.RootElement.EnumerateArray().ToArray();
            if (items.Length == 0)
            {
                errors.Add("Recipe parameter array cannot be empty.");
                return errors;
            }

            for (var index = 0; index < items.Length; index++)
            {
                ValidateItem(items[index], index, errors);
            }
        }

        return errors;
    }

    private static void ValidateItem(
        JsonElement item,
        int index,
        ICollection<string> errors)
    {
        var prefix = $"RecipeParameters[{index}]";

        if (item.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{prefix} must be an object.");
            return;
        }

        ValidateRequiredString(item, "id", prefix, errors);
        ValidateRequiredString(item, "name", prefix, errors);
        ValidateRequiredString(item, "unit", prefix, errors);

        var hasMin = TryReadDecimal(item, "min", prefix, errors, out var min);
        var hasMax = TryReadDecimal(item, "max", prefix, errors, out var max);

        if (hasMin && hasMax && min > max)
        {
            errors.Add($"{prefix}.min cannot be greater than {prefix}.max.");
        }
    }

    private static void ValidateRequiredString(
        JsonElement item,
        string propertyName,
        string prefix,
        ICollection<string> errors)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            errors.Add($"{prefix}.{propertyName} is required.");
            return;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{prefix}.{propertyName} must be a non-empty string.");
        }
    }

    private static bool TryReadDecimal(
        JsonElement item,
        string propertyName,
        string prefix,
        ICollection<string> errors,
        out decimal value)
    {
        value = default;

        if (!item.TryGetProperty(propertyName, out var property))
        {
            errors.Add($"{prefix}.{propertyName} is required.");
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDecimal(out value))
        {
            errors.Add($"{prefix}.{propertyName} must be numeric.");
            return false;
        }

        return true;
    }
}
