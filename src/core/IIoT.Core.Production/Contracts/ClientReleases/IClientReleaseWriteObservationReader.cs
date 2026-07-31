using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.Core.Production.Contracts.ClientReleases;

public sealed record ClientReleaseVersionWriteState(
    Guid ComponentId,
    Guid VersionId,
    string ContentSha256,
    uint ComponentRowVersion,
    uint VersionRowVersion,
    ClientReleaseStatus Status,
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason,
    string? DeletionFailure);

public sealed record ClientReleaseComponentWriteState(
    Guid ComponentId,
    uint RowVersion,
    string StateSha256);

public sealed record ClientReleaseDeletionWriteState(
    Guid Id,
    Guid ComponentId,
    string ComponentKind,
    string ComponentKey,
    string Channel,
    string TargetRuntime,
    string VersionsJson,
    string? Reason,
    Guid? RequestedByUserId,
    string? RequestedByUserName,
    ClientReleaseComponentDeletionStatus Status,
    string? FailureCode,
    int RetryCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? CleanupResultJson,
    DateTime? CleanupCompletedAtUtc,
    uint RowVersion,
    string FilesSha256);

public sealed record ClientReleaseComponentDeletionWriteObservation(
    ClientReleaseComponentWriteState? Component,
    ClientReleaseDeletionWriteState? Deletion);

public sealed record ClientReleaseRetentionPolicyWriteState(
    Guid Id,
    int MaxVersionsPerComponent,
    DateTime UpdatedAtUtc,
    uint RowVersion);

public sealed record DeviceBootstrapWriteState(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    Guid ProcessId,
    string? BootstrapSecretHash,
    uint RowVersion);

/// <summary>
/// Reads client-release mutation facts through a newly-created DbContext and one consistent
/// database snapshot. Callers use attempt observations with the execution-strategy callback
/// token and commit observations with an independent bounded token.
/// </summary>
public interface IClientReleaseWriteObservationReader
{
    Task<ClientReleaseVersionWriteState?> ObserveVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientReleaseVersionWriteState>> ObserveVersionsAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken);

    Task<ClientReleaseComponentWriteState?> ObserveComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken);

    Task<ClientReleaseComponentDeletionWriteObservation> ObserveComponentDeletionAsync(
        Guid componentId,
        Guid deletionId,
        CancellationToken cancellationToken);

    Task<ClientReleaseDeletionWriteState?> ObserveDeletionAsync(
        Guid deletionId,
        CancellationToken cancellationToken);

    Task<ClientReleaseRetentionPolicyWriteState?> ObserveRetentionPolicyAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceBootstrapWriteState>> ObserveDeviceBootstrapAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken);
}

public static class ClientReleaseWriteStateFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ClientReleaseVersionWriteState ForVersion(
        ClientReleaseComponent component,
        ClientReleaseVersion version)
        => new(
            component.Id,
            version.Id,
            Sha256(new
            {
                component.ComponentKind,
                component.ComponentKey,
                component.DisplayName,
                component.Description,
                component.IconKind,
                component.AccentColor,
                component.Channel,
                component.TargetRuntime,
                componentCreatedAtUtc = component.CreatedAtUtc,
                version.Id,
                version.ClientReleaseComponentId,
                version.Version,
                version.HostApiVersion,
                version.MinHostVersion,
                version.MaxHostVersion,
                version.TargetFramework,
                version.DownloadUrl,
                version.Sha256,
                version.PackageSize,
                version.ReleaseNotes,
                version.DependenciesJson,
                version.Signature,
                version.Publisher,
                versionCreatedAtUtc = version.CreatedAtUtc,
                artifacts = version.Artifacts
                    .OrderBy(artifact => artifact.ArtifactKind)
                    .ThenBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                    .Select(artifact => new
                    {
                        artifact.Id,
                        artifact.ClientReleaseVersionId,
                        artifact.ArtifactKind,
                        artifact.RelativePath,
                        artifact.Sha256,
                        artifact.Size,
                        artifact.CreatedAtUtc
                    })
                    .ToArray()
            }),
            component.RowVersion,
            version.RowVersion,
            version.Status,
            NormalizeUtc(version.PublishedAtUtc),
            NormalizeUtc(version.DeletedAtUtc),
            version.DeletionReason,
            version.DeletionFailure);

    public static ClientReleaseComponentWriteState ForComponent(
        ClientReleaseComponent component)
        => new(
            component.Id,
            component.RowVersion,
            Sha256(new
            {
                component.Id,
                component.ComponentKind,
                component.ComponentKey,
                component.DisplayName,
                component.Description,
                component.IconKind,
                component.AccentColor,
                component.Channel,
                component.TargetRuntime,
                component.CreatedAtUtc,
                component.UpdatedAtUtc,
                component.RowVersion,
                versions = component.Versions
                    .OrderBy(version => version.Id)
                    .Select(version => ForVersion(component, version))
                    .ToArray()
            }));

    public static ClientReleaseDeletionWriteState ForDeletion(
        ClientReleaseComponentDeletion deletion)
        => new(
            deletion.Id,
            deletion.ComponentId,
            deletion.ComponentKind,
            deletion.ComponentKey,
            deletion.Channel,
            deletion.TargetRuntime,
            CanonicalizeJson(deletion.VersionsJson),
            deletion.Reason,
            deletion.RequestedByUserId,
            deletion.RequestedByUserName,
            deletion.Status,
            deletion.FailureCode,
            deletion.RetryCount,
            NormalizeUtc(deletion.CreatedAtUtc),
            NormalizeUtc(deletion.UpdatedAtUtc),
            deletion.CleanupResultJson is null
                ? null
                : CanonicalizeJson(deletion.CleanupResultJson),
            NormalizeUtc(deletion.CleanupCompletedAtUtc),
            deletion.RowVersion,
            Sha256(deletion.Files
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new
                {
                    file.Id,
                    file.ClientReleaseComponentDeletionId,
                    file.RelativePath,
                    file.ArtifactKind,
                    file.Sha256,
                    file.SizeBytes
                })
                .ToArray()));

    private static string CanonicalizeJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonicalJson(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static void WriteCanonicalJson(
        Utf8JsonWriter writer,
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(
                                 property => property.Name,
                                 StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(
                    element.GetRawText(),
                    skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException(
                    $"Unsupported JSON value kind: {element.ValueKind}.");
        }
    }

    private static string Sha256<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? TruncateToPostgresMicrosecond(value)
            : TruncateToPostgresMicrosecond(
                DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTime? NormalizeUtc(DateTime? value)
        => value.HasValue ? NormalizeUtc(value.Value) : null;

    private static DateTime TruncateToPostgresMicrosecond(DateTime value)
        => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}
