using IIoT.EntityFrameworkCore;
using IIoT.DataWorker.Consumers;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IIoTDbContext>("iiot-db");

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