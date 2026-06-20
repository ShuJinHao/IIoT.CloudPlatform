using IIoT.Gateway.Infrastructure;
using IIoT.Infrastructure.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = builder.Configuration
        .GetValue<long?>("EdgeReleaseUpload:MaxBundleBytes")
        ?? 2L * 1024 * 1024 * 1024;
});

builder.AddSerilog("gateway");
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IGatewayRouteCatalog, GatewayRouteCatalog>();
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseIIoTSerilogRequestLogging();
app.UseMiddleware<GatewayObservabilityMiddleware>();
app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
