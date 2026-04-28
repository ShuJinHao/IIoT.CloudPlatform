using System.Diagnostics;
using System.Text;

namespace IIoT.EndToEndTests;

internal static class DockerTestResourceCleaner
{
    private const int MaxCleanupAttempts = 5;
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromSeconds(1);

    public static async Task CleanupNamedVolumesAsync(params string[] volumeNames)
    {
        var names = volumeNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var volumeName in names)
        {
            await CleanupNamedVolumeAsync(volumeName);
        }
    }

    private static async Task CleanupNamedVolumeAsync(string volumeName)
    {
        for (var attempt = 1; attempt <= MaxCleanupAttempts; attempt++)
        {
            await RemoveContainersUsingVolumeAsync(volumeName);

            var removeResult = await RunDockerAsync("volume", "rm", volumeName);
            if (removeResult.ExitCode == 0)
            {
                return;
            }

            if (!await VolumeExistsAsync(volumeName))
            {
                return;
            }

            await Task.Delay(CleanupRetryDelay);
        }

        Console.Error.WriteLine(
            $"[IIoT.EndToEndTests] Failed to remove test volume '{volumeName}' after {MaxCleanupAttempts} attempts.");
    }

    private static async Task RemoveContainersUsingVolumeAsync(string volumeName)
    {
        var listResult = await RunDockerAsync("ps", "-aq", "--filter", $"volume={volumeName}");
        if (listResult.ExitCode != 0 || string.IsNullOrWhiteSpace(listResult.StandardOutput))
        {
            return;
        }

        var containerIds = listResult.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var containerId in containerIds)
        {
            var removeResult = await RunDockerAsync("rm", "-f", containerId);
            if (removeResult.ExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"[IIoT.EndToEndTests] Failed to remove test container '{containerId}' while cleaning volume '{volumeName}'.");
            }
        }
    }

    private static async Task<bool> VolumeExistsAsync(string volumeName)
    {
        var inspectResult = await RunDockerAsync("volume", "inspect", volumeName);
        return inspectResult.ExitCode == 0;
    }

    private static async Task<DockerCommandResult> RunDockerAsync(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardOutput.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardError.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new DockerCommandResult(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString());
    }

    private sealed record DockerCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
