using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;

namespace IIoT.HttpApi.Infrastructure;

/// <summary>
/// 启动恢复：处理数据库提交与文件清理之间进程中断留下的永久删除操作。
/// 恢复失败不阻断宿主启动，操作保持 Failed 等待管理员显式重试。
/// </summary>
public sealed class ClientReleaseComponentDeletionRecoveryService(
    IServiceScopeFactory scopeFactory,
    ILogger<ClientReleaseComponentDeletionRecoveryService> logger) : BackgroundService
{
    internal static void RegisterUnlessExplicitlyDisabledForTesting(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var disabled = configuration.GetValue<bool>(
            "HttpApi:Testing:DisableClientReleaseComponentDeletionRecovery");
        if (disabled && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "HttpApi:Testing:DisableClientReleaseComponentDeletionRecovery is restricted to the Testing environment.");
        }

        if (!disabled)
        {
            services.AddHostedService<ClientReleaseComponentDeletionRecoveryService>();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var deletionStore = provider.GetRequiredService<IClientReleaseComponentDeletionStore>();
            var pending = await deletionStore.GetPendingAsync(stoppingToken);
            if (pending.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "Client release component deletion recovery started. Pending={PendingCount}.",
                pending.Count);

            var processor = provider.GetRequiredService<IClientReleaseComponentDeletionProcessor>();
            foreach (var deletion in pending)
            {
                try
                {
                    var outcome = await processor.ProcessAsync(deletion, stoppingToken);
                    if (!outcome.Succeeded)
                    {
                        logger.LogWarning(
                            new EventId(4613, "ClientReleaseComponentDeletionRecoveryFailure"),
                            "Client release component deletion recovery left operation Failed. DeletionId={DeletionId} FailureCode={FailureCode}.",
                            deletion.Id,
                            outcome.FailureCode);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 单个操作恢复失败不阻断其余操作，也不阻断宿主启动。
                    logger.LogError(
                        new EventId(4613, "ClientReleaseComponentDeletionRecoveryFailure"),
                        ex,
                        "Client release component deletion recovery iteration failed. DeletionId={DeletionId} ErrorType={ErrorType}.",
                        deletion.Id,
                        ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主关闭，操作保持待清理状态等待下次启动或管理员重试。
        }
        catch (Exception ex)
        {
            // 启动恢复本身绝不阻断宿主启动。
            logger.LogError(
                new EventId(4613, "ClientReleaseComponentDeletionRecoveryFailure"),
                ex,
                "Client release component deletion recovery failed to enumerate pending operations. ErrorType={ErrorType}.",
                ex.GetType().Name);
        }
    }
}
