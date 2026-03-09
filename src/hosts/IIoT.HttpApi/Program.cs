using IIoT.HttpApi;

var builder = WebApplication.CreateBuilder(args);

// 🌟 1. 全量注入逻辑
builder.AddServiceDefaults();
builder.AddApplicationService();
builder.AddWebServices();

builder.Services.AddControllers();
builder.Services.AddOpenApi(); // 🌟 .NET 10 内置 OpenAPI

var app = builder.Build();

// 🌟 2. 管道配置 (复刻 AI 示例)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();