namespace IIoT.EntityFrameworkCore.Identity;

public sealed class RefreshTokenSession
{
    public Guid Id { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public Guid SubjectId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevokedReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public uint RowVersion { get; set; }
}
