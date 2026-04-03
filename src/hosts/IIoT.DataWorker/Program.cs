using IIoT.DataWorker.Commands;
using IIoT.DataWorker.Consumers;
using IIoT.EntityFrameworkCore;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Logging;
using IIoT.Services.Common.Behaviors;
using MassTransit;
using MediatR;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// 1. Serilog 日志
builder.AddSerilog("dataworker");

// 2. Aspire 服务默认配置
builder.AddServiceDefaults();

// 3. 数据库上下文
builder.AddNpgsqlDbContext<IIoTDbContext>("iiot-db");

// 4. 基础设施（FusionCache L1+L2+Backplane + ICacheService + IDistributedLockService）
builder.AddInfrastructures();

// 5. MediatR
builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = "eyJhbGciOiJSUzI1NiIsImtpZCI6Ikx1Y2t5UGVubnlTb2Z0d2FyZUxpY2Vuc2VLZXkvYmJiMTNhY2I1OTkwNGQ4OWI0Y2IxYzg1ZjA4OGNjZjkiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL2x1Y2t5cGVubnlzb2Z0d2FyZS5jb20iLCJhdWQiOiJMdWNreVBlbm55U29mdHdhcmUiLCJleHAiOiIxODA0MTE4NDAwIiwiaWF0IjoiMTc3MjYwOTI4MCIsImFjY291bnRfaWQiOiIwMTljYjdiYTA0NGM3Y2FjYTcyZDNhMWQ3YjRlYzZjNiIsImN1c3RvbWVyX2lkIjoiY3RtXzAxa2p2dnk2ZjFjdjM1bmF3NzNmNGZ0MTE4Iiwic3ViX2lkIjoiLSIsImVkaXRpb24iOiIwIiwidHlwZSI6IjIifQ.vuSHJIt34rSumtJdD5ZI6gorKQmaD5Msk28ucJr2GIPFR1TsOqtyvdMydzyN5nFIEv_EeGNOu_LfTHTCDz2G-Vu9atS1h7xhIoQqNT8PvuLPHEHrf90YjOKEe4rxjohth1fC2SqpkvrJ0VzEPWQNsy5lvoLOZmzw2WAHa6NBy5bc4R9tQNwOUUbxLSwhmnyOo6K1Td87CBXEjAveGrXuSwhNE0NnQWuTs1ptcK40tfkq3T3Bigh2NO-QDiGuipxoS5AQIkO6n-wLjuhFW1078IEeyh9wct2l7s8htWNQLIlmRvFFJPiN2m1-cI60ds4SYfr4FA4pM6DSNXIMDMeGyA";
    cfg.RegisterServicesFromAssemblyContaining<UpsertHourlyCapacityHandler>();
    cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
});

// 6. MassTransit + RabbitMQ + Consumer 注册
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

    // 产能半小时：1 串行（+ 分布式锁双重保障多实例场景）
    x.AddConsumer<HourlyCapacityConsumer>(cfg =>
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