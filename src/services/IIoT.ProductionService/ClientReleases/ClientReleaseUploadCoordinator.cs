using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

public enum ClientReleaseUploadKind
{
    HostBundle,
    PluginPackage
}

public static class ClientReleaseUploadErrors
{
    public const string ConcurrentUpload = "已有 Edge 发布上传正在执行，请稍后重试。";
}

public sealed class ClientReleaseUploadCoordinator(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IOptions<EdgeReleaseUploadOptions> uploadOptions,
    IClientReleaseUploadSource source,
    ILogger<ClientReleaseUploadCoordinator> logger)
{
    public async ValueTask<ClientReleaseUploadSession?> TryBeginAsync(
        ClientReleaseUploadKind kind)
    {
        var (directoryName, fileName) = kind switch
        {
            ClientReleaseUploadKind.HostBundle => ("edge-release-bundles", "bundle.zip"),
            ClientReleaseUploadKind.PluginPackage => ("edge-plugin-packages", "plugin-package.zip"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知客户端发布上传类型。")
        };
        var edgeRoot = ResolveEdgeUpdatesRoot();
        Directory.CreateDirectory(edgeRoot);
        var uploadLock = TryAcquireUploadLock(Path.Combine(edgeRoot, ".edge-release-upload.lock"));
        if (uploadLock is null)
        {
            return null;
        }

        var stagingRoot = Path.Combine(
            edgeRoot,
            uploadOptions.Value.StagingDirectoryName,
            directoryName,
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingRoot);
            return new ClientReleaseUploadSession(
                kind,
                edgeRoot,
                stagingRoot,
                Path.Combine(stagingRoot, fileName),
                uploadOptions.Value.MaxBundleBytes,
                uploadOptions.Value.MaxUploadMbps,
                source,
                uploadLock,
                logger);
        }
        catch
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
            catch (Exception cleanupException)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    ClientReleasePublishDiagnostics.UploadSessionCreationCleanupFailed,
                    "session-creation-staging-cleanup",
                    cleanupException,
                    "staging-directory");
            }

            try
            {
                await uploadLock.DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    ClientReleasePublishDiagnostics.UploadSessionLockReleaseFailed,
                    "session-creation-lock-release",
                    cleanupException,
                    "shared-upload-lock");
            }

            throw;
        }
    }

    private string ResolveEdgeUpdatesRoot()
    {
        var installerRoot = Path.GetFullPath(artifactOptions.Value.RootPath);
        var parent = Directory.GetParent(installerRoot);
        if (parent is null)
        {
            throw new InvalidOperationException("EdgeInstallerArtifacts:RootPath 必须位于 edge-updates/installers 下。");
        }

        return parent.FullName;
    }

    private static FileStream? TryAcquireUploadLock(string lockPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }
}

public sealed class ClientReleaseUploadSession : IAsyncDisposable
{
    private readonly ClientReleaseUploadKind kind;
    private readonly long? declaredLength;
    private readonly long maxBytes;
    private readonly IClientReleaseUploadSource source;
    private readonly FileStream uploadLock;
    private readonly ILogger logger;
    private int receiveStarted;
    private int disposeStarted;

    internal ClientReleaseUploadSession(
        ClientReleaseUploadKind kind,
        string edgeRoot,
        string stagingRoot,
        string uploadedFilePath,
        long maxBytes,
        int maxUploadMbps,
        IClientReleaseUploadSource source,
        FileStream uploadLock,
        ILogger logger)
    {
        this.kind = kind;
        declaredLength = source.DeclaredLength;
        this.maxBytes = maxBytes;
        this.source = source;
        this.uploadLock = uploadLock;
        this.logger = logger;
        EdgeRoot = edgeRoot;
        StagingRoot = stagingRoot;
        UploadedFilePath = uploadedFilePath;
        MaxUploadMbps = maxUploadMbps;
        AuditSource = source.AuditSource;
    }

    public string EdgeRoot { get; }

    public string StagingRoot { get; }

    public string UploadedFilePath { get; }

    public int MaxUploadMbps { get; }

    public string? AuditSource { get; }

    public async Task<long> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref receiveStarted, 1) != 0)
        {
            throw new InvalidOperationException("客户端发布上传会话只能接收一次。");
        }

        var maxBytesPerSecond = Math.Max(1L, MaxUploadMbps) * 1024L * 1024L / 8L;
        var window = Stopwatch.StartNew();
        var windowBytes = 0L;
        var totalBytes = 0L;
        var buffer = new byte[1024 * 1024];

        await using var target = new FileStream(
            UploadedFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read < 0 || read > buffer.Length)
            {
                throw new ClientReleaseValidationException("客户端发布上传来源返回了非法读取长度。");
            }

            totalBytes += read;
            windowBytes += read;
            if (totalBytes > maxBytes)
            {
                throw new ClientReleaseValidationException($"{UploadLabel}超过最大限制 {maxBytes} 字节。");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (windowBytes >= maxBytesPerSecond)
            {
                var remaining = TimeSpan.FromSeconds(1) - window.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }

                window.Restart();
                windowBytes = 0;
            }
        }

        await target.FlushAsync(cancellationToken);
        if (declaredLength is { } expectedBytes && expectedBytes != totalBytes)
        {
            throw new ClientReleaseValidationException($"{UploadLabel}上传大小与声明长度不一致。");
        }

        return totalBytes;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            if (Directory.Exists(StagingRoot))
            {
                Directory.Delete(StagingRoot, recursive: true);
            }
        }
        catch (Exception exception)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.UploadSessionStagingCleanupFailed,
                "session-staging-cleanup",
                exception,
                "staging-directory");
        }

        try
        {
            await uploadLock.DisposeAsync();
        }
        catch (Exception exception)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Error,
                ClientReleasePublishDiagnostics.UploadSessionLockReleaseFailed,
                "session-lock-release",
                exception,
                "shared-upload-lock");
        }

        GC.SuppressFinalize(this);
    }

    private string UploadLabel => kind == ClientReleaseUploadKind.HostBundle
        ? "Edge 发布包"
        : "Edge 插件发布包";
}
