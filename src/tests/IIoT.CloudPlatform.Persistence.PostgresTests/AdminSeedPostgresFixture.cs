using System.Diagnostics;
using System.Security.Cryptography;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AdminSeedPostgresCollection
    : ICollectionFixture<AdminSeedPostgresFixture>
{
    public const string Name = "Admin seed isolated Postgres";
}

public sealed class AdminSeedPostgresFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(1);
    private readonly string containerName = $"iiot-admin-seed-{Guid.NewGuid():N}";

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "Aa1!";
        await RunDockerAsync(
            "run",
            "--detach",
            "--rm",
            "--name",
            containerName,
            "--publish",
            "127.0.0.1::5432",
            "--env",
            $"POSTGRES_PASSWORD={password}",
            "postgres:17-alpine");

        try
        {
            var portOutput = await RunDockerAsync("port", containerName, "5432/tcp");
            var portText = portOutput
                .Trim()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Last();
            var separator = portText.LastIndexOf(':');
            if (separator < 0 ||
                !int.TryParse(portText[(separator + 1)..], out var port))
            {
                throw new InvalidOperationException(
                    $"Docker did not expose a valid PostgreSQL port for {containerName}.");
            }

            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = port,
                Database = "postgres",
                Username = "postgres",
                Password = password,
                Pooling = true,
                Timeout = 5,
                CommandTimeout = 30
            }.ConnectionString;

            using var timeout = new CancellationTokenSource(StartupTimeout);
            Exception? lastException = null;
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync(timeout.Token);
                    return;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (
                    exception is NpgsqlException or TimeoutException)
                {
                    lastException = exception;
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            throw new TimeoutException(
                "Dedicated Admin seed PostgreSQL container did not become ready.",
                lastException);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await RunDockerAsync("rm", "--force", containerName);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(
                "No such container",
                StringComparison.OrdinalIgnoreCase))
        {
            // Docker --rm may already have removed the container.
        }
    }

    public Task<string> GetConnectionStringAsync()
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Admin seed PostgreSQL fixture is not initialized.");
        }

        return Task.FromResult(connectionString);
    }

    private static async Task<string> RunDockerAsync(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Docker CLI.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker command failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }
}
