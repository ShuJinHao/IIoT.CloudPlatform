using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

internal sealed class PostgresTestBudget : IDisposable
{
    private readonly CancellationTokenSource _timeout;

    private PostgresTestBudget(string connectionString, TimeSpan timeout)
    {
        ConnectionString = connectionString;
        _timeout = new CancellationTokenSource(timeout);
    }

    public string ConnectionString { get; }

    public CancellationToken Token => _timeout.Token;

    public static async Task<PostgresTestBudget> CreateAsync(
        ClientReleaseCommitRecoveryPostgresFixture fixture,
        TimeSpan? timeout = null) =>
        new(
            await fixture.GetConnectionStringAsync(),
            timeout ?? TimeSpan.FromSeconds(30));

    public static async Task RollbackAsync(NpgsqlTransaction transaction)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transaction.RollbackAsync(cleanup.Token);
    }

    public void Dispose() => _timeout.Dispose();
}
