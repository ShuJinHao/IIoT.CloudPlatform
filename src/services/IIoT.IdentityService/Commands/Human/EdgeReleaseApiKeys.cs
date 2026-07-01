using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record CreateEdgeReleaseApiKeyCommand(
    string Name,
    IReadOnlyCollection<string>? Permissions = null,
    DateTimeOffset? ExpiresAtUtc = null)
    : IHumanCommand<Result<EdgeReleaseApiKeyCreateResult>>;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record RevokeEdgeReleaseApiKeyCommand(
    Guid Id,
    string? Reason = null)
    : IHumanCommand<Result>;

public sealed class CreateEdgeReleaseApiKeyHandler(
    IEdgeReleaseApiKeyService apiKeyService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<CreateEdgeReleaseApiKeyCommand, Result<EdgeReleaseApiKeyCreateResult>>
{
    public async Task<Result<EdgeReleaseApiKeyCreateResult>> Handle(
        CreateEdgeReleaseApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        var actorUserId = ParseActorUserId(currentUser.Id);
        var result = await apiKeyService.CreateAsync(
            request.Name,
            request.Permissions,
            request.ExpiresAtUtc,
            actorUserId,
            cancellationToken);

        await auditTrailService.TryWriteAsync(
            CreateAuditEntry(request, result, actorUserId),
            cancellationToken);

        return result;
    }

    private AuditTrailEntry CreateAuditEntry(
        CreateEdgeReleaseApiKeyCommand request,
        Result<EdgeReleaseApiKeyCreateResult> result,
        Guid? actorUserId)
    {
        var created = result.Value!;
        var target = result.IsSuccess
            ? created.Id.ToString()
            : request.Name;
        var summary = result.IsSuccess
            ? $"Created Edge release API key '{request.Name}' with {created.Permissions.Count} permission(s), expires at {created.ExpiresAtUtc:O}."
            : $"Failed to create Edge release API key '{request.Name}'.";

        return new AuditTrailEntry(
            actorUserId,
            currentUser.UserName,
            "ClientRelease.ApiKey.Create",
            "EdgeReleaseApiKey",
            target,
            DateTime.UtcNow,
            result.IsSuccess,
            summary,
            result.IsSuccess ? null : JoinErrors(result.Errors, "Edge release API key creation failed."));
    }

    private static Guid? ParseActorUserId(string? userId)
        => Guid.TryParse(userId, out var parsed) ? parsed : null;

    private static string JoinErrors(IEnumerable<string>? errors, string fallback)
        => string.Join("; ", errors ?? [fallback]);
}

public sealed class RevokeEdgeReleaseApiKeyHandler(
    IEdgeReleaseApiKeyService apiKeyService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<RevokeEdgeReleaseApiKeyCommand, Result>
{
    public async Task<Result> Handle(
        RevokeEdgeReleaseApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        var actorUserId = ParseActorUserId(currentUser.Id);
        var result = await apiKeyService.RevokeAsync(
            request.Id,
            actorUserId,
            request.Reason,
            cancellationToken);

        await auditTrailService.TryWriteAsync(
            CreateAuditEntry(request, result, actorUserId),
            cancellationToken);

        return result;
    }

    private AuditTrailEntry CreateAuditEntry(
        RevokeEdgeReleaseApiKeyCommand request,
        Result result,
        Guid? actorUserId)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "not specified"
            : request.Reason.Trim();
        var summary = result.IsSuccess
            ? $"Revoked Edge release API key {request.Id}. Reason: {reason}."
            : $"Failed to revoke Edge release API key {request.Id}.";

        return new AuditTrailEntry(
            actorUserId,
            currentUser.UserName,
            "ClientRelease.ApiKey.Revoke",
            "EdgeReleaseApiKey",
            request.Id.ToString(),
            DateTime.UtcNow,
            result.IsSuccess,
            summary,
            result.IsSuccess ? null : JoinErrors(result.Errors, "Edge release API key revocation failed."));
    }

    private static Guid? ParseActorUserId(string? userId)
        => Guid.TryParse(userId, out var parsed) ? parsed : null;

    private static string JoinErrors(IEnumerable<string>? errors, string fallback)
        => string.Join("; ", errors ?? [fallback]);
}
