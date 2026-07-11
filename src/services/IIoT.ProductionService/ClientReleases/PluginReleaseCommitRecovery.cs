using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal enum PluginReleaseCommitObservationOutcome
{
    Committed,
    Conflict,
    Unknown
}

internal static class PluginReleaseCommitRecovery
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);

    public static async Task<PluginReleaseCommitObservationOutcome> ObserveAsync(
        IClientReleaseVersionObservationReader observationReader,
        ClientReleaseExpectedVersionState expected,
        PluginReleasePublishFileTransaction fileTransaction,
        ILogger logger)
    {
        ClientReleaseVersionObservation? observed;
        using var timeout = new CancellationTokenSource(ObservationTimeout);
        try
        {
            observed = (await observationReader.ObserveAsync([expected.Identity], timeout.Token))
                .SingleOrDefault();
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.PluginCommitUnknown,
                "plugin-commit-observation",
                ex,
                "plugin-release");
            return PluginReleaseCommitObservationOutcome.Unknown;
        }

        if (observed is null)
        {
            ClientReleasePublishDiagnostics.LogCondition(
                logger,
                ClientReleasePublishDiagnostics.PluginCommitUnknown,
                "plugin-commit-observation",
                "version-not-observed",
                "plugin-release");
            return PluginReleaseCommitObservationOutcome.Unknown;
        }

        if (!ClientReleaseExpectedVersionMatcher.Matches(expected, observed))
        {
            ClientReleasePublishDiagnostics.LogCondition(
                logger,
                ClientReleasePublishDiagnostics.PluginCommitConflict,
                "plugin-commit-observation",
                "persisted-state-mismatch",
                "plugin-release");
            return PluginReleaseCommitObservationOutcome.Conflict;
        }

        if (!fileTransaction.HasExactPublishedPackage())
        {
            return PluginReleaseCommitObservationOutcome.Unknown;
        }

        logger.LogInformation(
            ClientReleasePublishDiagnostics.PluginCommitRecovered,
            "Client release operation {Operation} confirmed the persisted and static plugin release effect for {Target}.",
            "plugin-commit-observation",
            "plugin-release");
        return PluginReleaseCommitObservationOutcome.Committed;
    }

}
