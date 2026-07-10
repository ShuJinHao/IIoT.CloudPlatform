using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Configuration;
using Npgsql;
using Xunit;

namespace IIoT.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    [WorkspaceAlignmentFact]
    [Trait("Category", "CloudAiReadWorkspaceAlignment")]
    public async Task CurrentCloudAndAICopilotTypedClient_ShouldPassWorkspaceAlignment()
    {
        var auditSinceUtc = DateTime.UtcNow.AddSeconds(-1);
        var channel = $"align-{Guid.NewGuid():N}"[..20];
        const string targetRuntime = "win-x64";
        var sentinel = $"x0-sensitive-{Guid.NewGuid():N}";
        var firstDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
        var secondDay = firstDay.AddDays(1);
        var startTime = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var endTime = new DateTimeOffset(secondDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        await AuthenticateAsAdminAsync();
        var runningDevice = await CreateTestDeviceRegistrationAsync("x0-running");
        var missingDevice = await CreateTestDeviceRegistrationAsync("x0-missing");
        var staleDevice = await CreateTestDeviceRegistrationAsync("x0-stale");

        await SeedClientReleaseVersionsAsync(channel, targetRuntime);
        await SeedReadOnlyBusinessDataAsync(runningDevice, firstDay, secondDay);
        await SeedRuntimeStateAsync(runningDevice, "Running", DateTime.UtcNow.AddMinutes(-1));
        await SeedRuntimeStateAsync(staleDevice, "Running", DateTime.UtcNow.AddHours(-25));

        var permissions = new[]
        {
            AiReadPermissions.Device,
            AiReadPermissions.Process,
            AiReadPermissions.ClientRelease,
            AiReadPermissions.DeviceClientState,
            AiReadPermissions.Capacity,
            AiReadPermissions.DeviceLog,
            AiReadPermissions.ProductionRecord
        };
        var fullToken = CreateAiReadToken(permissions);
        var stateOnlyToken = CreateAiReadToken([AiReadPermissions.DeviceClientState]);
        var forbiddenToken = CreateAiReadToken([]);

        var workspaceRoot = FindWorkspaceRoot();
        var liveProject = Path.Combine(
            workspaceRoot,
            "AICopilot",
            "src",
            "tests",
            "AICopilot.CloudAiReadLiveTests",
            "AICopilot.CloudAiReadLiveTests.csproj");
        File.Exists(liveProject).Should().BeTrue($"live test project should exist at {liveProject}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(workspaceRoot, "AICopilot"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(liveProject);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("--logger");
        startInfo.ArgumentList.Add("console;verbosity=minimal");

        var environment = startInfo.Environment;
        environment["CLOUD_AI_READ_LIVE_BASE_URL"] = _fixture.HttpClient.BaseAddress!.ToString();
        environment["CLOUD_AI_READ_LIVE_TOKEN"] = fullToken;
        environment["CLOUD_AI_READ_LIVE_STATE_ONLY_TOKEN"] = stateOnlyToken;
        environment["CLOUD_AI_READ_LIVE_FORBIDDEN_TOKEN"] = forbiddenToken;
        environment["CLOUD_AI_READ_LIVE_DEVICE_ID"] = runningDevice.DeviceId.ToString();
        environment["CLOUD_AI_READ_LIVE_MISSING_DEVICE_ID"] = missingDevice.DeviceId.ToString();
        environment["CLOUD_AI_READ_LIVE_STALE_DEVICE_ID"] = staleDevice.DeviceId.ToString();
        environment["CLOUD_AI_READ_LIVE_DEVICE_CODE"] = runningDevice.Code;
        environment["CLOUD_AI_READ_LIVE_PROCESS_ID"] = runningDevice.ProcessId.ToString();
        environment["CLOUD_AI_READ_LIVE_CHANNEL"] = channel;
        environment["CLOUD_AI_READ_LIVE_TARGET_RUNTIME"] = targetRuntime;
        environment["CLOUD_AI_READ_LIVE_HOURLY_DATE"] = firstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        environment["CLOUD_AI_READ_LIVE_START_TIME"] = startTime.ToString("O");
        environment["CLOUD_AI_READ_LIVE_END_TIME"] = endTime.ToString("O");
        environment["CLOUD_AI_READ_LIVE_SENTINEL"] = sentinel;

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start AICopilot live contract test process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0,
            "the real Cloud provider and AICopilot typed-client live matrix must pass.\nstdout:\n{0}\nstderr:\n{1}",
            stdout,
            stderr);

        var summaries = await ReadAiReadAuditSummariesSinceAsync(auditSinceUtc);
        summaries.Should().NotBeEmpty();
        summaries.Should().OnlyContain(summary => !summary.Contains(sentinel, StringComparison.Ordinal));
        summaries.Should().Contain(summary => summary.Contains("present", StringComparison.Ordinal));
    }

    private async Task SeedClientReleaseVersionsAsync(string channel, string targetRuntime)
    {
        await AuthenticateAsAdminAsync();
        for (var index = 1; index <= 2; index++)
        {
            await PostJsonAsync("/api/v1/human/client-releases/host-releases", new
            {
                Channel = channel,
                Version = $"1.0.{index}",
                HostApiVersion = "1.0.0",
                TargetRuntime = targetRuntime,
                TargetFramework = "net10.0",
                DownloadUrl = $"http://127.0.0.1/non-production/{channel}/1.0.{index}.zip",
                Sha256 = new string(index == 1 ? 'A' : 'B', 64),
                PackageSize = 1024 + index,
                ReleaseNotes = $"workspace alignment release {index}",
                Status = "Published",
                Signature = (string?)null,
                Publisher = "workspace-alignment"
            });
        }
    }

    private async Task SeedReadOnlyBusinessDataAsync(
        TestDeviceRegistration device,
        DateOnly firstDay,
        DateOnly secondDay)
    {
        await AuthenticateAsEdgeAsync(device.DeviceId);

        var firstTime = DateTime.SpecifyKind(firstDay.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        var secondTime = DateTime.SpecifyKind(firstDay.ToDateTime(new TimeOnly(11, 0)), DateTimeKind.Utc);
        var thirdTime = DateTime.SpecifyKind(secondDay.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);

        await PostJsonAsync("/api/v1/edge/device-logs", new
        {
            DeviceId = device.DeviceId,
            Logs = new[]
            {
                new { Level = "INFO", Message = "x0-live-log-1", LogTime = firstTime },
                new { Level = "WARN", Message = "x0-live-log-2", LogTime = secondTime }
            }
        });

        foreach (var item in new[]
                 {
                     (Date: firstDay, Time: firstTime, Total: 7, Ok: 6, Ng: 1),
                     (Date: firstDay, Time: secondTime, Total: 8, Ok: 7, Ng: 1),
                     (Date: secondDay, Time: thirdTime, Total: 9, Ok: 8, Ng: 1)
                 })
        {
            await PostJsonAsync("/api/v1/edge/capacity/hourly", new
            {
                DeviceId = device.DeviceId,
                item.Date,
                ShiftCode = "D",
                Hour = item.Time.Hour,
                Minute = item.Time.Minute,
                TimeLabel = item.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                TotalCount = item.Total,
                OkCount = item.Ok,
                NgCount = item.Ng,
                PlcName = "X0-PLC"
            });
        }

        await PostJsonAsync("/api/v1/edge/process-records", new
        {
            TypeKey = "injection",
            ProcessType = "injection",
            SchemaVersion = 1,
            DeviceId = device.DeviceId,
            Records = new[]
            {
                CreateAlignmentProcessRecord(device.DeviceId, "X0-LIVE-0001", firstTime, true, 1.1m),
                CreateAlignmentProcessRecord(device.DeviceId, "X0-LIVE-0002", secondTime, false, 1.2m)
            }
        });
    }

    private static object CreateAlignmentProcessRecord(
        Guid deviceId,
        string barcode,
        DateTime completedAt,
        bool result,
        decimal volume)
    {
        return new
        {
            TypeKey = "injection",
            ProcessType = "injection",
            SchemaVersion = 1,
            DeviceId = deviceId,
            Barcode = barcode,
            CellResult = result,
            CompletedTime = completedAt,
            Payload = new
            {
                PreInjectionTime = completedAt.AddSeconds(-20),
                PreInjectionWeight = 11.2m,
                PostInjectionTime = completedAt.AddSeconds(-5),
                PostInjectionWeight = 12.5m,
                InjectionVolume = volume
            }
        };
    }

    private async Task SeedRuntimeStateAsync(
        TestDeviceRegistration device,
        string status,
        DateTime reportedAtUtc)
    {
        await AuthenticateAsEdgeAsync(device.DeviceId);
        await PostJsonAsync("/api/v1/edge/runtime-heartbeats", new
        {
            DeviceId = device.DeviceId,
            ClientCode = device.Code,
            RuntimeInstanceId = $"x0-{Guid.NewGuid():N}",
            MachineProfile = "workspace-alignment",
            HostVersion = "1.0.0",
            HostApiVersion = "1.0.0",
            Status = status,
            StartedAtUtc = reportedAtUtc.AddMinutes(-5),
            ReportedAtUtc = DateTime.SpecifyKind(reportedAtUtc, DateTimeKind.Utc),
            LocalIpAddresses = new[] { "127.0.0.1" }
        });
    }

    private async Task<IReadOnlyList<string>> ReadAiReadAuditSummariesSinceAsync(DateTime sinceUtc)
    {
        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select "Summary"
            from audit_trails
            where "OperationType" = 'AiRead.Query'
              and "ExecutedAtUtc" >= @sinceUtc
            order by "ExecutedAtUtc"
            """,
            connection);
        command.Parameters.AddWithValue("sinceUtc", sinceUtc);
        await using var reader = await command.ExecuteReaderAsync();
        var summaries = new List<string>();
        while (await reader.ReadAsync())
        {
            summaries.Add(reader.GetString(0));
        }

        return summaries;
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "AICopilot")) &&
                Directory.Exists(Path.Combine(current.FullName, "IIoT.CloudPlatform")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the Cloud/AICopilot workspace root.");
    }
}

public sealed class WorkspaceAlignmentFactAttribute : FactAttribute
{
    public WorkspaceAlignmentFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("IIOT_RUN_CLOUD_AI_WORKSPACE_ALIGNMENT"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set IIOT_RUN_CLOUD_AI_WORKSPACE_ALIGNMENT=1 to run the real Cloud/AICopilot workspace alignment.";
        }
    }
}
