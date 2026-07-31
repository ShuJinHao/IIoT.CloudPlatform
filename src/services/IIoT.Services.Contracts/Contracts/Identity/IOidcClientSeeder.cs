namespace IIoT.Services.Contracts.Identity;

public interface IOidcClientSeeder
{
    Task<string> EnsureAicopilotClientAsync(CancellationToken cancellationToken = default);
}
