using IIoT.Dapper;
using IIoT.EntityFrameworkCore;
using IIoT.Infrastructure;
using IIoT.MigrationWorkApp;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Configuration;

var builder = Host.CreateApplicationBuilder(args);
_ = builder.Configuration.GetRequiredValidatedOptions<PostgresOptions>(
    PostgresOptions.SectionName,
    static options => options.Validate());
_ = builder.AddValidatedOptions<OidcProviderOptions>(
    OidcProviderOptions.SectionName,
    static options => options.Validate());

builder.AddServiceDefaults();
builder.AddInfrastructures();
builder.AddEfCore();
builder.AddDapper();

builder.Services.AddScoped<IDatabaseInitializationOrchestrator, DatabaseInitializationOrchestrator>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.Run();
