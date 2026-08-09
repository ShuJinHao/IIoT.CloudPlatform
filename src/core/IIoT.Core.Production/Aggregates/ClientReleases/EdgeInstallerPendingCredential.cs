using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

public enum EdgeInstallerPendingCredentialStatus
{
    Pending,
    Activating,
    Activated,
    Expired,
    Failed
}

public enum EdgeInstallerActivationAttempt
{
    Started,
    Replayed,
    Confirmed,
    AlreadyConfirmed,
    Expired,
    Conflict,
    Unavailable
}

public sealed class EdgeInstallerPendingCredential : BaseEntity<Guid>
{
    private EdgeInstallerPendingCredential()
    {
    }

    public EdgeInstallerPendingCredential(
        Guid generationId,
        Guid deviceId,
        string clientCode,
        string secretHash,
        string moduleId,
        string pluginVersion,
        string packageSha256,
        DateTime expiresAtUtc)
    {
        if (generationId == Guid.Empty || deviceId == Guid.Empty)
            throw new ArgumentException("GenerationId and DeviceId are required.");

        Id = Guid.NewGuid();
        GenerationId = generationId;
        DeviceId = deviceId;
        ClientCode = Required(clientCode).ToUpperInvariant();
        SecretHash = Required(secretHash);
        ModuleId = Required(moduleId);
        PluginVersion = Required(pluginVersion);
        PackageSha256 = NormalizeSha256(packageSha256);
        CreatedAtUtc = NormalizeUtc(DateTime.UtcNow);
        ExpiresAtUtc = NormalizeUtc(expiresAtUtc);
        if (ExpiresAtUtc <= CreatedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        Status = EdgeInstallerPendingCredentialStatus.Pending;
    }

    public Guid GenerationId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string ClientCode { get; private set; } = null!;
    public string SecretHash { get; private set; } = null!;
    public string ModuleId { get; private set; } = null!;
    public string PluginVersion { get; private set; } = null!;
    public string PackageSha256 { get; private set; } = null!;
    public EdgeInstallerPendingCredentialStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? ActivationStartedAtUtc { get; private set; }
    public int? ReadyProcessId { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public int ActivationReplayCount { get; private set; }
    public uint RowVersion { get; private set; }

    public bool IsUsableAt(DateTime nowUtc)
        => (Status is EdgeInstallerPendingCredentialStatus.Pending
                or EdgeInstallerPendingCredentialStatus.Activating)
           && ExpiresAtUtc > NormalizeUtc(nowUtc);

    public EdgeInstallerActivationAttempt BeginActivation(
        int processId,
        DateTime readyAtUtc,
        DateTime? activatedAtUtc = null)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        var now = NormalizeUtc(activatedAtUtc ?? DateTime.UtcNow);
        var normalizedReadyAtUtc = NormalizeUtc(readyAtUtc);
        if (Status == EdgeInstallerPendingCredentialStatus.Pending
            && ExpiresAtUtc <= now)
        {
            Status = EdgeInstallerPendingCredentialStatus.Expired;
            return EdgeInstallerActivationAttempt.Expired;
        }

        if (Status is EdgeInstallerPendingCredentialStatus.Activating
            or EdgeInstallerPendingCredentialStatus.Activated)
        {
            if (ReadyProcessId != processId
                || ReadyAtUtc != normalizedReadyAtUtc)
            {
                return EdgeInstallerActivationAttempt.Conflict;
            }

            if (Status == EdgeInstallerPendingCredentialStatus.Activated)
                return EdgeInstallerActivationAttempt.AlreadyConfirmed;

            ActivationReplayCount++;
            return EdgeInstallerActivationAttempt.Replayed;
        }

        if (Status != EdgeInstallerPendingCredentialStatus.Pending)
            return EdgeInstallerActivationAttempt.Unavailable;

        ReadyProcessId = processId;
        ReadyAtUtc = normalizedReadyAtUtc;
        ActivationStartedAtUtc = now;
        Status = EdgeInstallerPendingCredentialStatus.Activating;
        return EdgeInstallerActivationAttempt.Started;
    }

    public EdgeInstallerActivationAttempt ConfirmActivation(
        int processId,
        DateTime readyAtUtc,
        DateTime? confirmedAtUtc = null)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        var normalizedReadyAtUtc = NormalizeUtc(readyAtUtc);
        if (ReadyProcessId != processId || ReadyAtUtc != normalizedReadyAtUtc)
            return EdgeInstallerActivationAttempt.Conflict;

        if (Status == EdgeInstallerPendingCredentialStatus.Activated)
            return EdgeInstallerActivationAttempt.AlreadyConfirmed;
        if (Status != EdgeInstallerPendingCredentialStatus.Activating)
            return EdgeInstallerActivationAttempt.Unavailable;

        ActivatedAtUtc = NormalizeUtc(confirmedAtUtc ?? DateTime.UtcNow);
        Status = EdgeInstallerPendingCredentialStatus.Activated;
        return EdgeInstallerActivationAttempt.Confirmed;
    }

    public bool MarkExpired(DateTime nowUtc)
    {
        if (Status != EdgeInstallerPendingCredentialStatus.Pending
            || ExpiresAtUtc > NormalizeUtc(nowUtc))
            return false;

        Status = EdgeInstallerPendingCredentialStatus.Expired;
        return true;
    }

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = Required(value).ToLowerInvariant();
        if (normalized.Length != 64
            || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("PackageSha256 must be SHA-256.");
        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
