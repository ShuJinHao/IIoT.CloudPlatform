using IIoT.Services.Contracts.Identity;

namespace IIoT.EmployeeService.Commands.Employees;

internal static class EmployeeWriteCommitRecovery
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);

    public static async Task<EmployeeMutationObservation?> TryObserveAttemptAsync(
        IEmployeeMutationObservationReader observationReader,
        Guid employeeId,
        CancellationToken callbackCancellationToken)
    {
        callbackCancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            callbackCancellationToken);
        timeout.CancelAfter(ObservationTimeout);
        try
        {
            return await observationReader.ObserveAsync(
                employeeId,
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (callbackCancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(callbackCancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<EmployeeMutationObservation?> TryObserveCommitAsync(
        IEmployeeMutationObservationReader observationReader,
        Guid employeeId)
    {
        using var timeout = new CancellationTokenSource(ObservationTimeout);
        try
        {
            return await observationReader.ObserveAsync(
                employeeId,
                timeout.Token);
        }
        catch
        {
            return null;
        }
    }

    public static bool MatchesExact(
        EmployeeMutationObservation current,
        EmployeeMutationObservation expected)
        => current.EmployeeExists == expected.EmployeeExists
           && current.EmployeeIsActive == expected.EmployeeIsActive
           && string.Equals(
               current.EmployeeNo,
               expected.EmployeeNo,
               StringComparison.Ordinal)
           && string.Equals(
               current.EmployeeRealName,
               expected.EmployeeRealName,
               StringComparison.Ordinal)
           && current.EmployeeRowVersion == expected.EmployeeRowVersion
           && DeviceIdsAreEquivalent(
               current.EmployeeDeviceIds,
               expected.EmployeeDeviceIds)
           && current.AccountExists == expected.AccountExists
           && current.AccountIsEnabled == expected.AccountIsEnabled
           && string.Equals(
               current.AccountEmployeeNo,
               expected.AccountEmployeeNo,
               StringComparison.Ordinal)
           && string.Equals(
               current.AccountSecurityStamp,
               expected.AccountSecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(current.Roles, expected.Roles)
           && current.HasActiveHumanSessions
           == expected.HasActiveHumanSessions;

    public static bool IsOnboardTarget(
        EmployeeMutationObservation observation,
        string employeeNo)
        => observation.EmployeeExists
           && observation.AccountExists
           && string.Equals(
               observation.EmployeeNo,
               employeeNo,
               StringComparison.Ordinal)
           && string.Equals(
               observation.AccountEmployeeNo,
               employeeNo,
               StringComparison.Ordinal);

    public static bool IsAbsentBaseline(
        EmployeeMutationObservation observation)
        => !observation.EmployeeExists
           && !observation.AccountExists
           && !observation.HasActiveHumanSessions;

    public static bool IsTerminationTarget(
        EmployeeMutationObservation observation)
        => !observation.EmployeeExists
           && !observation.AccountExists
           && !observation.HasActiveHumanSessions
           && NormalizeRoles(observation.Roles).Length == 0
           && NormalizeDeviceIds(observation.EmployeeDeviceIds).Length == 0;

    private static bool RolesAreEquivalent(
        IEnumerable<string> left,
        IEnumerable<string> right)
        => NormalizeRoles(left).SequenceEqual(
            NormalizeRoles(right),
            StringComparer.OrdinalIgnoreCase);

    private static string[] NormalizeRoles(IEnumerable<string>? roles)
        => (roles ?? [])
            .Select(role => role?.Trim())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool DeviceIdsAreEquivalent(
        IEnumerable<Guid>? left,
        IEnumerable<Guid>? right)
        => NormalizeDeviceIds(left).SequenceEqual(NormalizeDeviceIds(right));

    private static Guid[] NormalizeDeviceIds(IEnumerable<Guid>? deviceIds)
        => (deviceIds ?? [])
            .Distinct()
            .OrderBy(deviceId => deviceId)
            .ToArray();
}
