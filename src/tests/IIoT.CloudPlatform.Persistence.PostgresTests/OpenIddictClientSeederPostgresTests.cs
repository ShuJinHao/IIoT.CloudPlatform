using System.Data.Common;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class OpenIddictClientSeederPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task OidcClientSeed_FirstWarmAndCommitLoss_ShouldConvergeToOneClientId()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var commitLoss = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateProvider(
            budget.ConnectionString,
            commitLoss);
        var clientId = $"aicopilot-tx-03b3-{Guid.NewGuid():N}";
        var options = new OidcProviderOptions
        {
            AicopilotClientId = clientId,
            AicopilotRedirectUris =
            [
                "https://aicopilot.example.test/signin-oidc"
            ],
            AicopilotPostLogoutRedirectUris =
            [
                "https://aicopilot.example.test/signout-callback-oidc"
            ]
        };

        await RunSeedStageAsync(
            provider,
            options,
            budget.Token);
        await RunSeedStageAsync(
            provider,
            options,
            budget.Token);

        Assert.Equal(1, commitLoss.ExceptionsThrown);
        await using var verificationScope = provider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        Assert.Equal(
            1,
            await dbContext.OpenIddictApplications
                .AsNoTracking()
                .CountAsync(
                    application => application.ClientId == clientId,
                    budget.Token));
    }

    private static async Task RunSeedStageAsync(
        ServiceProvider provider,
        OidcProviderOptions options,
        CancellationToken cancellationToken)
    {
        await using var strategyScope = provider.CreateAsyncScope();
        var strategyContext = strategyScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            async callbackToken =>
            {
                await using var attemptScope = provider.CreateAsyncScope();
                var services = attemptScope.ServiceProvider;
                var context = services.GetRequiredService<IIoTDbContext>();
                var manager = services
                    .GetRequiredService<IOpenIddictApplicationManager>();
                var seeder = new OpenIddictClientSeeder(
                    manager,
                    Options.Create(options));
                await using var transaction =
                    await context.Database.BeginTransactionAsync(callbackToken);
                var seededClientId =
                    await seeder.EnsureAicopilotClientAsync(callbackToken);
                Assert.True(await context.OpenIddictApplications
                    .AsNoTracking()
                    .AnyAsync(
                        application => application.ClientId == seededClientId,
                        callbackToken));
                await transaction.CommitAsync(callbackToken);
            },
            cancellationToken);
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        IInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IIoTDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromMilliseconds(50),
                    null));
            options.UseOpenIddict<Guid>();
            options.AddInterceptors(interceptor);
        });
        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IIoTDbContext>()
                    .ReplaceDefaultEntities<Guid>();
            });
        return services.BuildServiceProvider();
    }

    private sealed class ThrowOnceAfterCommitInterceptor
        : DbTransactionInterceptor
    {
        private int armed = 1;
        private int exceptionsThrown;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                throw new PostgresException(
                    "simulated OIDC seed commit confirmation loss",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.SerializationFailure);
            }

            return Task.CompletedTask;
        }
    }
}
