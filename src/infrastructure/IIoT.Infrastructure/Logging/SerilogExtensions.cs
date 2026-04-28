using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace IIoT.Infrastructure.Logging;

public static class SerilogExtensions
{
    private const int RollingFileRetentionCount = 30;
    private const long SingleLogFileSizeLimitBytes = 10 * 1024 * 1024;

    /// <summary>
    /// 统一配置 Serilog：控制台 + 按天滚动文件
    /// 所有服务调用这一个方法，日志格式统一
    /// </summary>
    /// <param name="builder">HostApplicationBuilder</param>
    /// <param name="serviceName">服务名称，用于日志文件命名（如 httpapi、dataworker）</param>
    public static void AddSerilog(this IHostApplicationBuilder builder, string serviceName)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        var seqOptions = builder.Configuration.GetSection(SeqOptions.SectionName).Get<SeqOptions>()
                         ?? new SeqOptions();

        seqOptions.Validate();

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDirectory, $"iiot-{serviceName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RollingFileRetentionCount,
                fileSizeLimitBytes: SingleLogFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ServiceName}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: true);

        if (seqOptions.Enabled)
        {
            loggerConfiguration.WriteTo.Seq(
                serverUrl: seqOptions.ServerUrl,
                apiKey: string.IsNullOrWhiteSpace(seqOptions.ApiKey) ? null : seqOptions.ApiKey);
        }

        Log.Logger = loggerConfiguration.CreateLogger();

        builder.Services.AddSerilog();
    }
}
