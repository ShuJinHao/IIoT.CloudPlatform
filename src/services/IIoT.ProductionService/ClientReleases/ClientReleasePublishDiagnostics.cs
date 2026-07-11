using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleasePublishDiagnostics
{
    public static readonly EventId UploadSessionCreationCleanupFailed = new(6101, nameof(UploadSessionCreationCleanupFailed));
    public static readonly EventId UploadSessionStagingCleanupFailed = new(6102, nameof(UploadSessionStagingCleanupFailed));
    public static readonly EventId HostPublishFailed = new(6110, nameof(HostPublishFailed));
    public static readonly EventId HostRetentionCleanupFailed = new(6111, nameof(HostRetentionCleanupFailed));
    public static readonly EventId HostRollbackCleanupFailed = new(6112, nameof(HostRollbackCleanupFailed));
    public static readonly EventId PluginPublishFailed = new(6120, nameof(PluginPublishFailed));
    public static readonly EventId PluginRetentionCleanupFailed = new(6121, nameof(PluginRetentionCleanupFailed));
    public static readonly EventId PluginRollbackCleanupFailed = new(6122, nameof(PluginRollbackCleanupFailed));
    public static readonly EventId PluginCommitRecovered = new(6123, nameof(PluginCommitRecovered));
    public static readonly EventId PluginCommitUnknown = new(6124, nameof(PluginCommitUnknown));
    public static readonly EventId PluginCommitConflict = new(6125, nameof(PluginCommitConflict));
    public static readonly EventId PluginRollbackOwnershipMismatch = new(6126, nameof(PluginRollbackOwnershipMismatch));
    public static readonly EventId PluginOwnershipMarkerCleanupFailed = new(6127, nameof(PluginOwnershipMarkerCleanupFailed));

    public static void LogFailure(
        ILogger logger,
        LogLevel level,
        EventId eventId,
        string operation,
        Exception exception,
        string target)
    {
        logger.Log(
            level,
            eventId,
            "Client release operation {Operation} failed for {Target}; ExceptionType={ExceptionType}.",
            operation,
            target,
            exception.GetType().Name);
    }

    public static void LogCondition(
        ILogger logger,
        EventId eventId,
        string operation,
        string condition,
        string target)
    {
        logger.LogWarning(
            eventId,
            "Client release operation {Operation} could not complete for {Target}; Condition={Condition}.",
            operation,
            target,
            condition);
    }
}

internal sealed class ClientReleaseValidationException(string message)
    : Exception(message)
{
    public string SafeMessage { get; } = message;
}
