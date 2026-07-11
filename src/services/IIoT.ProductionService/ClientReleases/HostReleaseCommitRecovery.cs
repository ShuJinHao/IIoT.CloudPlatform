using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal enum HostReleaseCommitObservationOutcome
{
    Committed,
    Conflict,
    Unknown
}

internal static class HostReleaseCommitRecovery
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);

    public static async Task<HostReleaseCommitObservationOutcome> ObserveAsync(
        IClientReleaseVersionObservationReader observationReader,
        IReadOnlyCollection<ClientReleaseExpectedVersionState> expectedVersions,
        InstallerReleasePublishFileTransaction installerTransaction,
        VelopackReleasePublishFileTransaction velopackTransaction,
        IReadOnlyCollection<PluginReleasePublishFileTransaction> pluginTransactions,
        ILogger logger)
    {
        IReadOnlyList<ClientReleaseVersionObservation> observations;
        using var timeout = new CancellationTokenSource(ObservationTimeout);
        try
        {
            observations = await observationReader.ObserveAsync(
                expectedVersions.Select(expected => expected.Identity).ToArray(),
                timeout.Token);
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.HostCommitUnknown,
                "host-commit-observation",
                ex,
                "host-release");
            return HostReleaseCommitObservationOutcome.Unknown;
        }

        foreach (var expected in expectedVersions)
        {
            var matchingIdentity = observations.Where(observed => IdentityMatches(expected.Identity, observed)).ToArray();
            if (matchingIdentity.Length > 1)
            {
                ClientReleasePublishDiagnostics.LogCondition(
                    logger,
                    ClientReleasePublishDiagnostics.HostCommitUnknown,
                    "host-commit-observation",
                    "duplicate-version-observation",
                    FormatIdentity(expected.Identity));
                return HostReleaseCommitObservationOutcome.Unknown;
            }

            if (matchingIdentity.Length == 1
                && !ClientReleaseExpectedVersionMatcher.Matches(expected, matchingIdentity[0]))
            {
                ClientReleasePublishDiagnostics.LogCondition(
                    logger,
                    ClientReleasePublishDiagnostics.HostCommitConflict,
                    "host-commit-observation",
                    "persisted-state-mismatch",
                    FormatIdentity(expected.Identity));
                return HostReleaseCommitObservationOutcome.Conflict;
            }
        }

        if (expectedVersions.Any(expected => observations.All(observed =>
                !IdentityMatches(expected.Identity, observed))))
        {
            ClientReleasePublishDiagnostics.LogCondition(
                logger,
                ClientReleasePublishDiagnostics.HostCommitUnknown,
                "host-commit-observation",
                "expected-version-not-observed",
                "host-release");
            return HostReleaseCommitObservationOutcome.Unknown;
        }

        if (!installerTransaction.HasExactPublishedDirectory()
            || !velopackTransaction.HasExactPublishedFiles()
            || pluginTransactions.Any(transaction => !transaction.HasExactPublishedPackage()))
        {
            ClientReleasePublishDiagnostics.LogCondition(
                logger,
                ClientReleasePublishDiagnostics.HostCommitUnknown,
                "host-commit-observation",
                "static-effect-mismatch",
                "host-release");
            return HostReleaseCommitObservationOutcome.Unknown;
        }

        logger.LogInformation(
            ClientReleasePublishDiagnostics.HostCommitRecovered,
            "Client release operation {Operation} confirmed the persisted and static host release effects for {Target}.",
            "host-commit-observation",
            "host-release");
        return HostReleaseCommitObservationOutcome.Committed;
    }

    private static bool IdentityMatches(
        ClientReleaseVersionIdentity identity,
        ClientReleaseVersionObservation observation)
        => observation.ComponentKind == identity.ComponentKind
           && string.Equals(observation.ComponentKey, identity.ComponentKey, StringComparison.Ordinal)
           && string.Equals(observation.Channel, identity.Channel, StringComparison.Ordinal)
           && string.Equals(observation.TargetRuntime, identity.TargetRuntime, StringComparison.Ordinal)
           && string.Equals(observation.Version, identity.Version, StringComparison.Ordinal);

    private static string FormatIdentity(ClientReleaseVersionIdentity identity)
        => $"{identity.ComponentKind}/{identity.Channel}/{identity.ComponentKey}/{identity.TargetRuntime}/{identity.Version}";
}
