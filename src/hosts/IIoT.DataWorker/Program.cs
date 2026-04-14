using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper;
using IIoT.DataWorker.Consumers;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Logging;
using IIoT.ProductionService;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Common.Behaviors;
using IIoT.Services.Common.Events.PassStations;
using MassTransit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.ConfigureContainer(
    new DefaultServiceProviderFactory(new ServiceProviderOptions
    {
        ValidateOnBuild = false,
        ValidateScopes = false
    }));

builder.AddSerilog("dataworker");
builder.AddServiceDefaults();
builder.AddDapper();
builder.AddInfrastructures();

builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = "eyJhbGciOiJSUzI1NiIsImtpZCI6Ikx1Y2t5UGVubnlTb2Z0d2FyZUxpY2Vuc2VLZXkvYmJiMTNhY2I1OTkwNGQ4OWI0Y2IxYzg1ZjA4OGNjZjkiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL2x1Y2t5cGVubnlzb2Z0d2FyZS5jb20iLCJhdWQiOiJMdWNreVBlbm55U29mdHdhcmUiLCJleHAiOiIxODA0MTE4NDAwIiwiaWF0IjoiMTc3MjYwOTI4MCIsImFjY291bnRfaWQiOiIwMTljYjdiYTA0NGM3Y2FjYTcyZDNhMWQ3YjRlYzZjNiIsImN1c3RvbWVyX2lkIjoiY3RtXzAxa2p2dnk2ZjFjdjM1bmF3NzNmNGZ0MTE4Iiwic3ViX2lkIjoiLSIsImVkaXRpb24iOiIwIiwidHlwZSI6IjIifQ.vuSHJIt34rSumtJdD5ZI6gorKQmaD5Msk28ucJr2GIPFR1TsOqtyvdMydzyN5nFIEv_EeGNOu_LfTHTCDz2G-Vu9atS1h7xhIoQqNT8PvuLPHEHrf90YjOKEe4rxjohth1fC2SqpkvrJ0VzEPWQNsy5lvoLOZmzw2WAHa6NBy5bc4R9tQNwOUUbxLSwhmnyOo6K1Td87CBXEjAveGrXuSwhNE0NnQWuTs1ptcK40tfkq3T3Bigh2NO-QDiGuipxoS5AQIkO6n-wLjuhFW1078IEeyh9wct2l7s8htWNQLIlmRvFFJPiN2m1-cI60ds4SYfr4FA4pM6DSNXIMDMeGyA";
    cfg.RegisterServicesFromAssemblyContaining<ReceiveHourlyCapacityCommand>();
    cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
});

builder.Services.AddPassStationType<
    PassDataInjectionReceivedEvent,
    InjectionWriteModel,
    InjectionMapper>();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<PassStationConsumer<PassDataInjectionReceivedEvent>>(cfg => { cfg.ConcurrentMessageLimit = 4; });
    x.AddConsumer<DeviceLogConsumer>(cfg => { cfg.ConcurrentMessageLimit = 3; });
    x.AddConsumer<HourlyCapacityConsumer>(cfg => { cfg.ConcurrentMessageLimit = 1; });

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("eventbus");
        cfg.Host(connectionString);

        // Consumer 成功返回后确认消息；抛异常时按重试策略处理。
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
