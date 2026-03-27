using IIoT.HttpApi;
using IIoT.Infrastructure.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog("httpapi");
builder.AddServiceDefaults();
builder.AddApplicationService();
builder.AddWebServices();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();