using IIoT.Dapper;
using IIoT.EntityFrameworkCore;
using IIoT.Infrastructure;
using IIoT.MigrationWorkApp;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructures();
builder.AddEfCore();
builder.AddDapper();

builder.Services.AddScoped<IDatabaseInitializationOrchestrator, DatabaseInitializationOrchestrator>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.Run();
