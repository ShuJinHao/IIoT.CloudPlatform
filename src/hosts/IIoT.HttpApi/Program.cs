using IIoT.HttpApi;
using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Infrastructure.OpenApi;
using IIoT.Infrastructure.Logging;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog("httpapi");
builder.AddServiceDefaults();
builder.AddApplicationService();
builder.AddWebServices();

builder.Services.AddControllers()
    .AddMvcOptions(options =>
    {
        options.Conventions.Add(new RouteSurfaceApiExplorerConvention());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new PagedListJsonConverterFactory());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("human", new OpenApiInfo { Title = "IIoT Human API", Version = "v1" });
    options.SwaggerDoc("edge", new OpenApiInfo { Title = "IIoT Edge API", Version = "v1" });
    options.SwaggerDoc("bootstrap", new OpenApiInfo { Title = "IIoT Bootstrap API", Version = "v1" });
    options.DocInclusionPredicate((documentName, apiDescription) =>
        string.Equals(apiDescription.GroupName, documentName, StringComparison.OrdinalIgnoreCase));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/human/swagger.json", "human");
        options.SwaggerEndpoint("/swagger/edge/swagger.json", "edge");
        options.SwaggerEndpoint("/swagger/bootstrap/swagger.json", "bootstrap");
    });
}

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseForwardedHeaders();
app.UseCors(HttpApiCorsOptions.PolicyName);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
