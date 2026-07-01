namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EdgeReleaseApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    public string Status { get; set; } = EdgeReleaseApiKeyStatuses.Active;

    public string PermissionsJson { get; set; } = "[]";

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevokedReason { get; set; }
}

public static class EdgeReleaseApiKeyStatuses
{
    public const string Active = "Active";
    public const string Revoked = "Revoked";
}
