using System.Text.Json;

namespace IIoT.ProductionService.ClientReleases;

public sealed record PluginDataCapabilityField(
    string Name,
    string DataType,
    bool Nullable,
    bool IsPublic);

public sealed record PluginDataCapability(
    string TypeKey,
    string DisplayName,
    int SchemaVersion,
    string SchemaName,
    string Scope,
    IReadOnlyList<string> LegacyTypeKeys,
    IReadOnlyList<string> QueryModes,
    IReadOnlyList<PluginDataCapabilityField> Fields);

public static class PluginDataCapabilityCatalog
{
    public static IReadOnlyList<PluginDataCapability> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Plugin data capabilities must be a JSON array.");
        }

        var capabilities = new List<PluginDataCapability>();
        var primaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var typeKey = RequiredString(item, "typeKey");
            if (!primaryKeys.Add(typeKey) || aliases.Contains(typeKey))
            {
                throw new InvalidDataException(
                    $"Duplicate plugin TypeKey '{typeKey}'.");
            }

            var legacyTypeKeys = StringArray(item, "legacyTypeKeys");
            foreach (var alias in legacyTypeKeys)
            {
                if (primaryKeys.Contains(alias) || !aliases.Add(alias))
                {
                    throw new InvalidDataException(
                        $"Duplicate legacy TypeKey alias '{alias}'.");
                }
            }

            var publicFields = StringArray(item, "publicFields")
                .ToHashSet(StringComparer.Ordinal);
            var fieldsProperty = item.GetProperty("fields");
            if (fieldsProperty.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"Plugin TypeKey '{typeKey}' fields must be an array.");
            }

            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<PluginDataCapabilityField>();
            foreach (var field in fieldsProperty.EnumerateArray())
            {
                var name = RequiredString(field, "name");
                var dataType = RequiredString(field, "dataType");
                if (!fieldNames.Add(name)
                    || dataType is not (
                        "string" or "datetime" or "integer"
                        or "decimal" or "boolean"))
                {
                    throw new InvalidDataException(
                        $"Plugin TypeKey '{typeKey}' contains an invalid field.");
                }

                var nullable = field.GetProperty("nullable");
                if (nullable.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException(
                        $"Plugin TypeKey '{typeKey}' nullable must be boolean.");
                }

                fields.Add(new PluginDataCapabilityField(
                    name,
                    dataType,
                    nullable.GetBoolean(),
                    publicFields.Contains(name)));
            }

            if (publicFields.Any(field => !fieldNames.Contains(field)))
            {
                throw new InvalidDataException(
                    $"Plugin TypeKey '{typeKey}' exposes an unknown field.");
            }

            capabilities.Add(new PluginDataCapability(
                typeKey,
                RequiredString(item, "displayName"),
                PositiveInt(item, "schemaVersion"),
                RequiredString(item, "schemaName"),
                RequiredString(item, "scope"),
                legacyTypeKeys,
                StringArray(item, "queryModes"),
                fields));
        }

        return capabilities;
    }

    public static PluginDataCapability? Resolve(
        IReadOnlyList<PluginDataCapability> capabilities,
        string typeKey)
    {
        var normalized = typeKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return capabilities.SingleOrDefault(capability =>
            string.Equals(
                capability.TypeKey,
                normalized,
                StringComparison.OrdinalIgnoreCase)
            || capability.LegacyTypeKeys.Any(alias => string.Equals(
                alias,
                normalized,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static string RequiredString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"Plugin capability '{propertyName}' is required.");
        }

        return property.GetString()!.Trim();
    }

    private static int PositiveInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt32(out var value)
            || value <= 0)
        {
            throw new InvalidDataException(
                $"Plugin capability '{propertyName}' must be positive.");
        }

        return value;
    }

    private static IReadOnlyList<string> StringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Plugin capability '{propertyName}' must be an array.");
        }

        var values = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String
                ? item.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(value) || !unique.Add(value))
            {
                throw new InvalidDataException(
                    $"Plugin capability '{propertyName}' contains an invalid value.");
            }

            values.Add(value);
        }

        return values;
    }
}
