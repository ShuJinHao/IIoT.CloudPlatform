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
        Assert.Contains(
            "if (TryEnsureWritableDirectory(logDirectory))",
            productionSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "IIoT file logging disabled because log directory is not writable",
            productionSource,
            StringComparison.Ordinal);
        Assert.Contains("fileSizeLimitBytes: SingleLogFileSizeLimitBytes", productionSource, StringComparison.Ordinal);
        Assert.Contains("rollOnFileSizeLimit: true", productionSource, StringComparison.Ordinal);

        var serviceName = $"contract-{Guid.NewGuid():N}";
        var productionLogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            try
            {
                var builder = Host.CreateApplicationBuilder();
                builder.AddSerilog(serviceName);
                Log.Information(
                    "Production wiring credential {@Credential}",
                    new CredentialProbe("production-wiring-secret"));

                var payload = new string('x', 1024 * 1024);
                for (var index = 0; index < 12; index++)
                {
                    Log.Information("rolling-event-{Index} {Payload}", index, payload);
                }

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
            Assert.True(productionFiles.Length >= 2, "The production AddSerilog entry must roll after 10 MB.");
            Assert.True(productionFiles.Sum(path => new FileInfo(path).Length) > 10 * 1024 * 1024);
            var productionLog = string.Join(Environment.NewLine, productionFiles.Select(File.ReadAllText));
            Assert.DoesNotContain("production-wiring-secret", productionLog, StringComparison.Ordinal);
            Assert.Contains(SensitiveDataDestructuringPolicy.RedactedValue, productionLog, StringComparison.Ordinal);
        }
        finally
        {
            Log.CloseAndFlush();
            Console.SetOut(originalOutput);
            foreach (var productionFile in Directory.GetFiles(
                         productionLogDirectory,
                         $"iiot-{serviceName}-*.log"))
            {
                File.Delete(productionFile);
            }
        }
    }

    private sealed record CredentialProbe(string Password);

}
