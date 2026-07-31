namespace IIoT.Services.Contracts.Identity;

public interface IHumanSessionRevocationService
{
    Task RevokeAllAsync(
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default);
}

public interface IIndependentHumanSessionRevocationService
{
    Task RevokeAllAsync(
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default);
}
