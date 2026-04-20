using IIoT.Gateway.Infrastructure;
using IIoT.Infrastructure.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog("gateway");
builder.AddServiceDefaults();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<GatewayObservabilityMiddleware>();
app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
