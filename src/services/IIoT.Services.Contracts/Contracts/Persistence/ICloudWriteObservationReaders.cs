using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Services.Contracts.Persistence;

public sealed record ProcessWriteState(
    Guid Id,
    string ProcessCode,
    string ProcessName,
    uint RowVersion);

public sealed record ProcessWriteObservation(
    ProcessWriteState? Target,
    Guid? ProcessCodeOwnerId,
    bool HasDevices,
    bool HasRecipes);

public interface IProcessWriteObservationReader
{
    Task<ProcessWriteObservation> ObserveProcessAsync(
        Guid processId,
        string processCode,
        CancellationToken cancellationToken);
}

public sealed record DeviceWriteState(
    Guid Id,
    string DeviceName,
    string ClientCode,
    Guid ProcessId,
    uint RowVersion);

public sealed record DeviceWriteObservation(
    DeviceWriteState? Target,
    Guid? DeviceNameOwnerId,
    Guid? ClientCodeOwnerId,
    bool ProcessExists,
    DeviceDeletionImpact DeletionImpact);

public interface IDeviceWriteObservationReader
{
    Task<DeviceWriteObservation> ObserveDeviceAsync(
        Guid deviceId,
        string deviceName,
        string clientCode,
        Guid processId,
        CancellationToken cancellationToken);
}

public sealed record RecipeWriteState(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb,
    int Status,
    uint RowVersion);

public sealed record RecipeWriteObservation(
    RecipeWriteState? Target,
    IReadOnlyList<RecipeWriteState> Family,
    bool ProcessExists,
    bool DeviceExistsInProcess);

public interface IRecipeWriteObservationReader
{
    Task<RecipeWriteObservation> ObserveRecipeAsync(
        Guid recipeId,
        Guid processId,
        Guid deviceId,
        string recipeName,
        CancellationToken cancellationToken);
}

public sealed record DeviceReportState(
    DateTime ReportedAtUtc,
    DateTime ReceivedAtUtc,
    string ContentSha256);

public sealed record DeviceReportWriteObservation(
    bool DeviceExists,
    string? ClientCode,
    DeviceReportState? Version,
    DeviceReportState? RuntimeHeartbeat,
    DeviceReportState? PlcSnapshot);

public interface IDeviceReportWriteObservationReader
{
    Task<DeviceReportWriteObservation> ObserveReportAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken);
}
