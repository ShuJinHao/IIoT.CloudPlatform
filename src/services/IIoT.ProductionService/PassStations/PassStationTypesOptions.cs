using System.Text.RegularExpressions;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.ProductionService.PassStations;

public sealed partial class PassStationTypesOptions
{
    public const string SectionName = "PassStationTypes";

    public List<PassStationTypeDefinitionDto> Types { get; set; } = [];

    public void Validate()
    {
        if (Types.Count == 0)
            throw new InvalidOperationException("PassStationTypes must define at least one pass station type.");

        var typeKeys = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in Types)
        {
            ValidateType(type);
            var typeKey = Normalize(type.TypeKey);
            if (!typeKeys.Add(typeKey) || aliases.Contains(typeKey))
                throw new InvalidOperationException($"PassStation type '{type.TypeKey}' is duplicated.");
            foreach (var legacyTypeKey in type.LegacyTypeKeys)
            {
                var alias = Normalize(legacyTypeKey);
                if (!IsSafeTypeKey(alias)
                    || typeKeys.Contains(alias)
                    || !aliases.Add(alias))
                {
                    throw new InvalidOperationException(
                        $"PassStation legacy type key '{legacyTypeKey}' is invalid or duplicated.");
                }
            }
        }
    }

    private static void ValidateType(PassStationTypeDefinitionDto type)
    {
        if (!IsSafeTypeKey(type.TypeKey))
            throw new InvalidOperationException($"PassStation type key '{type.TypeKey}' is invalid.");
        if (string.IsNullOrWhiteSpace(type.DisplayName))
            throw new InvalidOperationException($"PassStation type '{type.TypeKey}' must define DisplayName.");
        if (type.Fields.Count == 0)
            throw new InvalidOperationException($"PassStation type '{type.TypeKey}' must define Fields.");
        if (type.ListColumns.Count == 0)
            throw new InvalidOperationException($"PassStation type '{type.TypeKey}' must define ListColumns.");
        if (type.DetailSections.Count == 0)
            throw new InvalidOperationException($"PassStation type '{type.TypeKey}' must define DetailSections.");

        var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in type.Fields)
        {
            ValidateField(type.TypeKey, field);
            if (!fieldKeys.Add(field.Key))
                throw new InvalidOperationException($"PassStation type '{type.TypeKey}' field '{field.Key}' is duplicated.");
        }

        var knownKeys = new HashSet<string>(fieldKeys, StringComparer.Ordinal)
        {
            "barcode",
            "cellResult",
            "completedTime",
            "receivedAt",
            "deviceId"
        };

        foreach (var column in type.ListColumns)
        {
            if (!knownKeys.Contains(column))
                throw new InvalidOperationException($"PassStation type '{type.TypeKey}' list column '{column}' is not defined.");
        }

        foreach (var section in type.DetailSections)
        {
            if (string.IsNullOrWhiteSpace(section.Title))
                throw new InvalidOperationException($"PassStation type '{type.TypeKey}' detail section title is required.");

            foreach (var field in section.Fields)
            {
                if (!knownKeys.Contains(field))
                    throw new InvalidOperationException($"PassStation type '{type.TypeKey}' detail field '{field}' is not defined.");
            }
        }

        if (type.SupportedModes.Count == 0)
            throw new InvalidOperationException($"PassStation type '{type.TypeKey}' must define SupportedModes.");

        foreach (var mode in type.SupportedModes)
        {
            if (!PassStationQueryModes.All.Contains(mode))
                throw new InvalidOperationException($"PassStation type '{type.TypeKey}' query mode '{mode}' is invalid.");
        }
    }

    private static void ValidateField(string typeKey, PassStationFieldDefinitionDto field)
    {
        if (!IsSafeFieldKey(field.Key))
            throw new InvalidOperationException($"PassStation type '{typeKey}' field key '{field.Key}' is invalid.");
        if (string.IsNullOrWhiteSpace(field.Label))
            throw new InvalidOperationException($"PassStation type '{typeKey}' field '{field.Key}' must define Label.");

        if (!PassStationFieldTypes.All.Contains(field.Type))
            throw new InvalidOperationException($"PassStation type '{typeKey}' field '{field.Key}' type '{field.Type}' is invalid.");

        if (field.Type == PassStationFieldTypes.Enum && (field.Options is null || field.Options.Count == 0))
            throw new InvalidOperationException($"PassStation type '{typeKey}' enum field '{field.Key}' must define Options.");

        if (field.Min is not null && field.Max is not null && field.Min > field.Max)
            throw new InvalidOperationException($"PassStation type '{typeKey}' field '{field.Key}' min cannot exceed max.");
    }

    private static bool IsSafeTypeKey(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && SafeTypeKeyPattern().IsMatch(value);
    }

    private static bool IsSafeFieldKey(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && SafeFieldKeyPattern().IsMatch(value);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")]
    private static partial Regex SafeTypeKeyPattern();

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex SafeFieldKeyPattern();
}

public static class PassStationFieldTypes
{
    public const string String = "string";
    public const string Number = "number";
    public const string Integer = "integer";
    public const string Boolean = "boolean";
    public const string DateTime = "datetime";
    public const string Enum = "enum";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        String,
        Number,
        Integer,
        Boolean,
        DateTime,
        Enum
    };
}
