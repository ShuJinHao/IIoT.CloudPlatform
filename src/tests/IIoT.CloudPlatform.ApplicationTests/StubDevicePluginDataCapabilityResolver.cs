using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Result;

namespace IIoT.CloudPlatform.ApplicationTests;

internal sealed class StubDevicePluginDataCapabilityResolver(
    IPassStationSchemaProvider schemaProvider)
    : IDevicePluginDataCapabilityResolver
{
    public string ClientCode { get; init; } = "EDGE-01";

    public string ProcessType { get; init; } = "cp";

    public string PluginVersion { get; init; } = "2.0.12";

    public Func<Guid, string, Result<ResolvedDevicePluginDataCapability>>?
        ResolveOverride { get; init; }

    public Task<Result<ResolvedDevicePluginDataCapability>> ResolveAsync(
        Guid deviceId,
        string typeKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ResolveOverride is not null)
            return Task.FromResult(ResolveOverride(deviceId, typeKey));

        var definition = schemaProvider.Find(typeKey);
        if (definition is null)
        {
            Result<ResolvedDevicePluginDataCapability> missing =
                Result.NotFound($"业务记录类别 [{typeKey}] 不存在。");
            return Task.FromResult(missing);
        }

        var capability = new PluginDataCapability(
            definition.TypeKey,
            definition.DisplayName,
            1,
            "pass-station-record",
            "plc",
            definition.LegacyTypeKeys,
            definition.SupportedModes,
            definition.Fields.Select(field => new PluginDataCapabilityField(
                field.Key,
                ToCapabilityType(field.Type),
                !field.Required,
                true)).ToArray());

        return Task.FromResult(Result.Success(
            new ResolvedDevicePluginDataCapability(
                ClientCode,
                ProcessType,
                PluginVersion,
                capability)));
    }

    private static string ToCapabilityType(string type)
        => type switch
        {
            PassStationFieldTypes.DateTime => "datetime",
            PassStationFieldTypes.Integer => "integer",
            PassStationFieldTypes.Number => "decimal",
            PassStationFieldTypes.Boolean => "boolean",
            _ => "string"
        };
}
