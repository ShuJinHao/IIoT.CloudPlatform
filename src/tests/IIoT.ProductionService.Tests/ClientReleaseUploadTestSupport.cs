using IIoT.ProductionService.ClientReleases;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Tests;

internal sealed class ClientReleaseUploadTestSource : IClientReleaseUploadSource
{
    private byte[] payload = [];
    private int offset;

    public long? DeclaredLength { get; private set; }

    public string? AuditSource { get; private set; }

    public int ReadCount { get; private set; }

    public bool CancelOnRead { get; set; }

    public void LoadFile(
        string path,
        long? declaredLength = null,
        string? auditSource = "127.0.0.1")
    {
        LoadBytes(File.ReadAllBytes(path), declaredLength, auditSource);
    }

    public void LoadBytes(
        byte[] content,
        long? declaredLength = null,
        string? auditSource = "127.0.0.1")
    {
        payload = content;
        offset = 0;
        ReadCount = 0;
        DeclaredLength = declaredLength ?? content.LongLength;
        AuditSource = auditSource;
    }

    public ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        cancellationToken.ThrowIfCancellationRequested();
        if (CancelOnRead)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var count = Math.Min(buffer.Length, payload.Length - offset);
        if (count == 0)
        {
            return ValueTask.FromResult(0);
        }

        payload.AsMemory(offset, count).CopyTo(buffer);
        offset += count;
        return ValueTask.FromResult(count);
    }
}

internal static class ClientReleaseUploadTestSupport
{
    public static ClientReleaseUploadCoordinator CreateCoordinator(
        string edgeRoot,
        IClientReleaseUploadSource source,
        long maxBundleBytes = EdgeReleaseUploadOptions.DefaultMaxBundleBytes)
    {
        return new ClientReleaseUploadCoordinator(
            Options.Create(new EdgeInstallerArtifactOptions
            {
                RootPath = Path.Combine(edgeRoot, "installers")
            }),
            Options.Create(new EdgeReleaseUploadOptions
            {
                MaxUploadMbps = 1000,
                MaxBundleBytes = maxBundleBytes,
                StagingDirectoryName = ".staging"
            }),
            source,
            NullLogger<ClientReleaseUploadCoordinator>.Instance);
    }
}
