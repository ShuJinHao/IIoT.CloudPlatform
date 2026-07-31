using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Services.CrossCutting.Persistence;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseWriteCommitRecovery
{
    public static bool MatchesVersionTarget(
        ClientReleaseVersionWriteState current,
        ClientReleaseVersionWriteState baseline,
        ClientReleaseStatus status,
        DateTime? deletedAtUtc = null,
        string? deletionReason = null,
        string? deletionFailure = null,
        string? deletionReceiptJson = null)
        => current.ComponentId == baseline.ComponentId
           && current.VersionId == baseline.VersionId
           && string.Equals(
               current.ContentSha256,
               baseline.ContentSha256,
               StringComparison.Ordinal)
           && current.Status == status
           && current.PublishedAtUtc == baseline.PublishedAtUtc
           && current.DeletedAtUtc == NormalizeUtc(deletedAtUtc)
           && string.Equals(
               current.DeletionReason,
               NormalizeOptional(deletionReason),
               StringComparison.Ordinal)
           && string.Equals(
               current.DeletionFailure,
               NormalizeOptional(deletionFailure),
               StringComparison.Ordinal)
           && JsonEqualsNullable(
               current.DeletionReceiptJson,
               NormalizeOptional(deletionReceiptJson));

    public static bool MatchesDeletionTarget(
        ClientReleaseDeletionWriteState current,
        ClientReleaseDeletionWriteState expected)
        => current.Id == expected.Id
           && current.ComponentId == expected.ComponentId
           && current.ComponentKind == expected.ComponentKind
           && current.ComponentKey == expected.ComponentKey
           && current.Channel == expected.Channel
           && current.TargetRuntime == expected.TargetRuntime
           && CloudWriteCommitRecovery.JsonEquals(
               current.VersionsJson,
               expected.VersionsJson)
           && current.Reason == expected.Reason
           && current.RequestedByUserId == expected.RequestedByUserId
           && current.RequestedByUserName == expected.RequestedByUserName
           && current.Status == expected.Status
           && current.FailureCode == expected.FailureCode
           && current.RetryCount == expected.RetryCount
           && current.CreatedAtUtc == expected.CreatedAtUtc
           && current.UpdatedAtUtc == expected.UpdatedAtUtc
           && JsonEqualsNullable(
               current.CleanupResultJson,
               expected.CleanupResultJson)
           && current.CleanupCompletedAtUtc
           == expected.CleanupCompletedAtUtc
           && current.FilesSha256 == expected.FilesSha256;

    public static bool MatchesDeviceBootstrapTarget(
        IReadOnlyCollection<DeviceBootstrapWriteState> current,
        IReadOnlyCollection<DeviceBootstrapWriteState> baseline,
        IReadOnlyDictionary<Guid, string> targetHashes)
    {
        if (current.Count != baseline.Count
            || current.Count != targetHashes.Count)
        {
            return false;
        }

        var baselineById = baseline.ToDictionary(item => item.DeviceId);
        return current.All(item =>
            baselineById.TryGetValue(item.DeviceId, out var original)
            && item.DeviceName == original.DeviceName
            && item.ClientCode == original.ClientCode
            && item.ProcessId == original.ProcessId
            && targetHashes.TryGetValue(item.DeviceId, out var targetHash)
            && string.Equals(
                item.BootstrapSecretHash,
                targetHash,
                StringComparison.Ordinal));
    }

    public static bool MatchesDeviceBootstrapBaseline(
        IReadOnlyCollection<DeviceBootstrapWriteState> current,
        IReadOnlyCollection<DeviceBootstrapWriteState> baseline)
        => current
            .OrderBy(item => item.DeviceId)
            .SequenceEqual(baseline.OrderBy(item => item.DeviceId));

    public static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(
            utc.Ticks - utc.Ticks % 10,
            DateTimeKind.Utc);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
        => value.HasValue ? NormalizeUtc(value.Value) : null;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool JsonEqualsNullable(
        string? left,
        string? right)
        => left is null || right is null
            ? left == right
            : CloudWriteCommitRecovery.JsonEquals(left, right);
}
