using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts.Auditing;
using Microsoft.Extensions.Options;

namespace IIoT.HttpApi.Infrastructure;

internal sealed class EdgeInstallerReadyPackageCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<EdgeInstallerArtifactOptions> options,
    ILogger<EdgeInstallerReadyPackageCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Ready installer package cleanup cycle failed; it will retry.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var generatedRoot = Path.GetFullPath(Path.Combine(
            options.Value.RootPath,
            "generated"));
        if (!Directory.Exists(generatedRoot))
            return;

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider
            .GetRequiredService<IEdgeInstallerGenerationStore>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrailService>();
        var candidates = await store.GetPackageCleanupCandidateIdsAsync(
            DateTime.UtcNow,
            500,
            cancellationToken);
        foreach (var generationId in candidates)
        {
            var path = ResolvePath(generatedRoot, generationId);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        exception,
                        "Ready installer cleanup will retry GenerationId={GenerationId}.",
                        generationId);
                    continue;
                }
            }

            var audited = await audit.TryWriteConfirmedAsync(
                new AuditTrailEntry(
                    null,
                    "system",
                    "EdgeInstaller.ReadyPackageCleaned",
                    "EdgeInstallerGeneration",
                    generationId.ToString("D"),
                    DateTime.UtcNow,
                    true,
                    "Pending credentials are all activated, terminal, or expired; removed secret-bearing ready package bytes.",
                    IdempotencyKey: $"edge-installer-ready-cleaned:{generationId:N}"),
                cancellationToken);
            if (!audited)
            {
                logger.LogWarning(
                    "Ready installer bytes were removed but cleanup audit is not yet confirmed; retrying next cycle. GenerationId={GenerationId}.",
                    generationId);
            }
        }

        // A crash can leave bytes before the immutable generation record is
        // committed. Only old, parseable generated files with no DB evidence
        // are treated as orphans; recent files are left for an in-flight build.
        foreach (var file in Directory.EnumerateFiles(
                     generatedRoot,
                     "*.exe",
                     SearchOption.TopDirectoryOnly))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)
                || File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.Subtract(TimeSpan.FromHours(1))
                || await store.GetByIdAsync(id, cancellationToken) is not null)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                var audited = await audit.TryWriteConfirmedAsync(
                    new AuditTrailEntry(
                        null,
                        "system",
                        "EdgeInstaller.OrphanPackageCleaned",
                        "EdgeInstallerGeneration",
                        id.ToString("D"),
                        DateTime.UtcNow,
                        true,
                        "Removed an old generated package that had no immutable generation record.",
                        IdempotencyKey: $"edge-installer-orphan-cleaned:{id:N}"),
                    cancellationToken);
                if (!audited)
                {
                    logger.LogWarning(
                        "Orphan installer bytes were removed but cleanup audit is not confirmed. GenerationId={GenerationId}.",
                        id);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Orphan installer cleanup will retry {Path}.", file);
            }
        }
    }

    private static string ResolvePath(string generatedRoot, Guid generationId)
    {
        var path = Path.GetFullPath(Path.Combine(
            generatedRoot,
            $"{generationId:N}.exe"));
        if (!path.StartsWith(
                generatedRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Generated package cleanup path escaped root.");
        }

        return path;
    }
}
