namespace IIoT.ProductionService.ClientReleases;

public interface IClientReleaseUploadSource
{
    long? DeclaredLength { get; }

    string? AuditSource { get; }

    ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default);
}
