using IIoT.Dapper;
using IIoT.EntityFrameworkCore;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Logging;
using IIoT.MigrationWorkApp;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// 1. Serilog 日志
builder.AddSerilog("migration");

// 2. 基础设施
builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

builder.AddEfCore();
builder.AddDapper();
builder.AddInfrastructures();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

var host = builder.Build();
host.Run();