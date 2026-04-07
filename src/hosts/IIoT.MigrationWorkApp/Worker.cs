using System.Diagnostics;
using IIoT.Dapper.Initializers;
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
    public const string ActivitySourceName = "IIoT.Migrations";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var recordSchemaInitializer = scope.ServiceProvider.GetRequiredService<RecordSchemaInitializer>();

            // 1. 执行 EF Core 迁移建表（仅聚合根/身份相关）
            await RunMigrationAsync(dbContext, cancellationToken);

            // 2. 执行 Dapper 记录表建表
            await InitializeRecordSchemasAsync(recordSchemaInitializer, cancellationToken);

            // 3. 初始化 TimescaleDB 扩展和时序表
            await InitializeTimescaleDbAsync(dbContext, cancellationToken);

            // 4. 播种初始数据
            await SeedDataAsync(dbContext, userManager, roleManager, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            logger.LogCritical(ex, "数据库初始化过程中发生异常！");
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private async Task RunMigrationAsync(IIoTDbContext dbContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("开始应用 EF Core 数据库迁移...");

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });

        logger.LogInformation("数据库迁移应用完成！");
    }

    private async Task InitializeRecordSchemasAsync(
        RecordSchemaInitializer recordSchemaInitializer,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("开始初始化记录表 Schema...");
        await recordSchemaInitializer.InitializeAsync(cancellationToken);
        logger.LogInformation("记录表 Schema 初始化完成！");
    }

    private async Task InitializeTimescaleDbAsync(IIoTDbContext dbContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("开始初始化 TimescaleDB...");

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS timescaledb;", cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM timescaledb_information.hypertables 
                        WHERE hypertable_name = 'pass_data_injection'
                    ) THEN
                        PERFORM create_hypertable('pass_data_injection', 'completed_time');
                    END IF;
                END $$;", cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM timescaledb_information.hypertables 
                        WHERE hypertable_name = 'device_logs'
                    ) THEN
                        PERFORM create_hypertable('device_logs', 'log_time');
                    END IF;
                END $$;", cancellationToken);

            logger.LogInformation("TimescaleDB 初始化完成：pass_data_injection 和 device_logs 已转为时序表");
        });
    }

    private async Task SeedDataAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("开始播种初始数据...");

        await SystemInitData.SeedAsync(dbContext, userManager, roleManager);

        logger.LogInformation("所有初始数据播种完成！");
    }
}