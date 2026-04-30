using IIoT.Services.Contracts.RecordQueries;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.PassStations;

public sealed class PassStationSchemaProvider : IPassStationSchemaProvider
{
    private readonly IReadOnlyList<PassStationTypeDefinitionDto> _types;
    private readonly Dictionary<string, PassStationTypeDefinitionDto> _lookup;

    public PassStationSchemaProvider(IOptions<PassStationTypesOptions> options)
    {
        options.Value.Validate();
        _types = options.Value.Types
            .Select(NormalizeDefinition)
            .OrderBy(x => x.TypeKey, StringComparer.Ordinal)
            .ToArray();
        _lookup = _types.ToDictionary(x => x.TypeKey, StringComparer.Ordinal);
    }

    public IReadOnlyList<PassStationTypeDefinitionDto> GetAll()
    {
        return _types;
    }

    public PassStationTypeDefinitionDto? Find(string typeKey)
    {
        return _lookup.GetValueOrDefault(Normalize(typeKey));
    }

    private static PassStationTypeDefinitionDto NormalizeDefinition(PassStationTypeDefinitionDto definition)
    {
        return new PassStationTypeDefinitionDto
        {
            TypeKey = Normalize(definition.TypeKey),
            DisplayName = definition.DisplayName.Trim(),
            Description = definition.Description.Trim(),
            Fields = definition.Fields
                .Select(field => new PassStationFieldDefinitionDto
                {
                    Key = field.Key.Trim(),
                    Label = field.Label.Trim(),
                    Type = field.Type.Trim(),
                    Required = field.Required,
                    MaxLength = field.MaxLength,
                    Min = field.Min,
                    Max = field.Max,
                    Unit = field.Unit,
                    Precision = field.Precision,
                    Options = field.Options
                })
                .ToList(),
            ListColumns = definition.ListColumns.Select(x => x.Trim()).ToList(),
            DetailSections = definition.DetailSections
                .Select(section => new PassStationDetailSectionDto
                {
                    Title = section.Title.Trim(),
                    Fields = section.Fields.Select(x => x.Trim()).ToList()
                })
                .ToList(),
            SupportedModes = definition.SupportedModes.Select(Normalize).ToList()
        };
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
