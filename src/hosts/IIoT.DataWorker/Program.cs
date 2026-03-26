using IIoT.EntityFrameworkCore;
using IIoT.DataWorker.Consumers;
using IIoT.Infrastructure.Logging;
using MassTransit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// 1. Serilog 日志（必须最先注册）
builder.AddSerilog("dataworker");

// 2. Aspire 服务默认配置
builder.AddServiceDefaults();

// 3. 数据库上下文
builder.AddNpgsqlDbContext<IIoTDbContext>("iiot-db");

// 4. MassTransit + RabbitMQ + Consumer 注册
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    // 过站数据：4 并发
    x.AddConsumer<PassDataInjectionConsumer>(cfg =>
    {
        cfg.ConcurrentMessageLimit = 4;
    });

    // 设备日志：3 并发
    x.AddConsumer<DeviceLogConsumer>(cfg =>
    {
        cfg.ConcurrentMessageLimit = 3;
    });

    // 产能汇总：1 串行
    x.AddConsumer<DailyCapacityConsumer>(cfg =>
    {
        cfg.ConcurrentMessageLimit = 1;
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("eventbus");
        cfg.Host(connectionString);

        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();