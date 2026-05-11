namespace IIoT.Services.Contracts.Identity;

public interface IOidcClientSeeder
{
    Task EnsureAicopilotClientAsync(CancellationToken cancellationToken = default);
}
