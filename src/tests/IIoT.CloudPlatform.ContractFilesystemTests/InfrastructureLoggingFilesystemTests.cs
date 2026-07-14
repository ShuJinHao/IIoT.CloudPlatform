using IIoT.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;
using Xunit;

namespace IIoT.CloudPlatform.ContractFilesystemTests;

public sealed class InfrastructureLoggingFilesystemTests
{
    [Fact]
    public void SerilogFileSink_ShouldRejectUnwritablePathAndRollAtTenMegabytes()
    {
        var productionSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.Infrastructure", "Logging", "SerilogExtensions.cs"));
        var addSerilogStart = productionSource.IndexOf(
            "public static void AddSerilog",
            StringComparison.Ordinal);
        var rollingHelperStart = productionSource.IndexOf(
            "internal static void ConfigureRollingFileSink",
            StringComparison.Ordinal);
        Assert.True(addSerilogStart >= 0 && rollingHelperStart > addSerilogStart);
        Assert.Contains(
            "ConfigureRollingFileSink(loggerConfiguration, logDirectory, serviceName);",
            productionSource[addSerilogStart..rollingHelperStart],
            StringComparison.Ordinal);

        var root = Path.Combine(Path.GetTempPath(), $"iiot-serilog-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var serviceName = $"contract-{Guid.NewGuid():N}";
            var productionLogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            var originalOutput = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                var builder = Host.CreateApplicationBuilder();
                builder.AddSerilog(serviceName);
                Log.Information(
                    "Production wiring credential {@Credential}",
                    new CredentialProbe("production-wiring-secret"));
                Log.CloseAndFlush();
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            Assert.DoesNotContain("production-wiring-secret", output.ToString(), StringComparison.Ordinal);
            Assert.Contains(SensitiveDataDestructuringPolicy.RedactedValue, output.ToString(), StringComparison.Ordinal);
            var productionFiles = Directory.GetFiles(
                productionLogDirectory,
                $"iiot-{serviceName}-*.log");
            Assert.NotEmpty(productionFiles);
            var productionLog = string.Join(Environment.NewLine, productionFiles.Select(File.ReadAllText));
            Assert.DoesNotContain("production-wiring-secret", productionLog, StringComparison.Ordinal);
            Assert.Contains(SensitiveDataDestructuringPolicy.RedactedValue, productionLog, StringComparison.Ordinal);
            foreach (var productionFile in productionFiles)
            {
                File.Delete(productionFile);
            }

            var blockingFile = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blockingFile, "block child directory creation");
            var originalError = Console.Error;
            using var error = new StringWriter();
            Console.SetError(error);
            try
            {
                Assert.False(SerilogExtensions.TryEnsureWritableDirectory(Path.Combine(blockingFile, "logs")));
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.Contains(
                "IIoT file logging disabled because log directory is not writable",
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(10 * 1024 * 1024, SerilogExtensions.SingleLogFileSizeLimitBytes);
            Assert.Equal(30, SerilogExtensions.RollingFileRetentionCount);

            var logDirectory = Path.Combine(root, "logs");
            Directory.CreateDirectory(logDirectory);
            var configuration = new LoggerConfiguration().MinimumLevel.Verbose();
            SerilogExtensions.ConfigureRollingFileSink(configuration, logDirectory, "contract");
            using (var logger = configuration.CreateLogger())
            {
                var payload = new string('x', 1024 * 1024);
                for (var index = 0; index < 12; index++)
                {
                    logger.Information("rolling-event-{Index} {Payload}", index, payload);
                }
            }

            var files = Directory.GetFiles(logDirectory, "iiot-contract-*.log");
            Assert.True(files.Length >= 2, "The production file sink must roll after the 10 MB limit.");
            Assert.True(files.Sum(path => new FileInfo(path).Length) > SerilogExtensions.SingleLogFileSizeLimitBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record CredentialProbe(string Password);

}
