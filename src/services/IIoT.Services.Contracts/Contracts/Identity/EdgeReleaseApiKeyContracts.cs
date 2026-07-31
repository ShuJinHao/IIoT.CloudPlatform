using IIoT.SharedKernel.Result;

namespace IIoT.Services.Contracts.Identity;

public sealed record EdgeReleaseApiKeyCreateResult(
    Guid Id,
    string Name,
    string ApiKey,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Permissions);

public sealed record EdgeReleaseApiKeyListItem(
    Guid Id,
    string Name,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason,
    IReadOnlyList<string> Permissions);

public sealed record EdgeReleaseApiKeyValidationResult(
    Guid Id,
    string Name,
    IReadOnlyList<string> Permissions);

public sealed record EdgeReleaseApiKeyAuditContext(
    string? ActorEmployeeNo,
    DateTime ExecutedAtUtc);

public interface IEdgeReleaseApiKeyService
{
    Task<Result<EdgeReleaseApiKeyCreateResult>> CreateAsync(
        string name,
        IReadOnlyCollection<string>? permissions,
        DateTimeOffset? expiresAtUtc,
        Guid? createdByUserId,
        EdgeReleaseApiKeyAuditContext auditContext,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EdgeReleaseApiKeyListItem>> GetListAsync(
        CancellationToken cancellationToken = default);

    Task<Result> RevokeAsync(
        Guid id,
        Guid? revokedByUserId,
        string? reason,
        EdgeReleaseApiKeyAuditContext auditContext,
        CancellationToken cancellationToken = default);

    Task<Result<EdgeReleaseApiKeyValidationResult>> ValidateAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}
