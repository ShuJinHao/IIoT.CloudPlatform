using IIoT.SharedKernel.Result;

namespace IIoT.Services.Contracts.Identity;

public sealed record RefreshTokenEnvelope(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record RefreshTokenRotationResult(
    string ActorType,
    Guid SubjectId,
    RefreshTokenEnvelope RefreshToken);

public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshTokens";

    public int HumanTtlDays { get; set; } = 7;
    public int EdgeBootstrapTtlDays { get; set; } = 30;

    public void Validate()
    {
        if (HumanTtlDays <= 0)
        {
            throw new InvalidOperationException("Refresh token human TTL must be greater than zero.");
        }

        if (EdgeBootstrapTtlDays <= 0)
        {
            throw new InvalidOperationException("Refresh token edge/bootstrap TTL must be greater than zero.");
        }
    }
}

public interface IRefreshTokenService
{
    Task<RefreshTokenEnvelope> IssueAsync(
        string actorType,
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<Result<RefreshTokenRotationResult>> RotateAsync(
        string actorType,
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task RevokeSubjectTokensAsync(
        string actorType,
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default);
}
