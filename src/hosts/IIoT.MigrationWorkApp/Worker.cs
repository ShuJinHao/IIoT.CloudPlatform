using System.Diagnostics;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.MigrationWorkApp.SeedData;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.MigrationWorkApp;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<Worker> logger) : BackgroundService
{
    // 🌟 修复 Program.cs 报错的核心：定义链路追踪的源名称和实例
    public const string ActivitySourceName = "IIoT.Migrations";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // 开启链路追踪 Activity
        using var activity = ActivitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            // 1. 执行数据库迁移建表
            await RunMigrationAsync(dbContext, cancellationToken);

            // 2. 集中执行所有种子数据的播种
            await SeedDataAsync(dbContext, userManager, roleManager, cancellationToken);
        }
        catch (Exception ex)
        {
            // 将异常信息记录到分布式链路追踪里
            activity?.AddException(ex);
            logger.LogCritical(ex, "数据库初始化过程中发生异常！");
            throw;
        }

        // 干完活立刻关闭自己
        hostApplicationLifetime.StopApplication();
    }

    private async Task RunMigrationAsync(IIoTDbContext dbContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("开始应用 EF Core 数据库迁移...");

        // 💡 架构师经验：在微服务和容器启动时，数据库可能还没彻底 Ready。
        // 使用 ExecutionStrategy 可以实现启动时的自动重试，防止一启动就报错崩溃。
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });

        logger.LogInformation("数据库迁移应用完成！");
    }

    private async Task SeedDataAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("开始播种初始数据...");

        // 1. 播种系统管理员与初始员工 (就是咱们刚才写的 101650 那个)
        await SystemInitData.SeedAsync(dbContext, userManager, roleManager);

        // 🌟 2. 后期如果有更多数据要播种，直接在这里往下加即可！比如：
        // await ProcessDictData.SeedAsync(dbContext); // 播种默认的几道工序
        // await DefaultRecipeData.SeedAsync(dbContext); // 播种出厂预置配方

        logger.LogInformation("所有初始数据播种完成！");
    }
}