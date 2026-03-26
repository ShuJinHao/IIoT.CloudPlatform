using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace IIoT.Infrastructure.Logging;

public static class SerilogExtensions
{
    /// <summary>
    /// 统一配置 Serilog：控制台 + 按天滚动文件
    /// 所有服务调用这一个方法，日志格式统一
    /// </summary>
    /// <param name="builder">HostApplicationBuilder</param>
    /// <param name="serviceName">服务名称，用于日志文件命名（如 httpapi、dataworker）</param>
    public static void AddSerilog(this IHostApplicationBuilder builder, string serviceName)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDirectory, $"iiot-{serviceName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ServiceName}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();

        builder.Services.AddSerilog();
    }
}