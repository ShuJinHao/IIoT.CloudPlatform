using IIoT.Dapper.Bootstrap;
using IIoT.Dapper.Initializers;
using IIoT.EntityFrameworkCore;
using IIoT.MigrationWorkApp;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// EF Core 负责聚合根 schema (由 EF Migration 执行)
builder.AddEfCore();

// Dapper 负责记录表 schema (由 RecordSchemaInitializer 执行)
builder.AddDapper();

// 注册 Worker(如果有)
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// 在启动时执行一次记录表 schema 初始化
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<RecordSchemaInitializer>();
    await initializer.InitializeAsync();
}

app.Run();