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
    options => options.Validate(builder.Environment.EnvironmentName));

builder.AddServiceDefaults();
builder.AddInfrastructures();
builder.AddEfCore();
builder.AddDapper();

builder.Services.AddScoped<IDatabaseInitializationOrchestrator, DatabaseInitializationOrchestrator>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.Run();
