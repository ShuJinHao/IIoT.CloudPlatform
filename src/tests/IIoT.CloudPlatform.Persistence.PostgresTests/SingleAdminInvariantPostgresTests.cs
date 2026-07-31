using System.Data.Common;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.MigrationWorkApp;
using IIoT.MigrationWorkApp.SeedData;
using IIoT.Services.Contracts.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenIddict.EntityFrameworkCore;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class SingleAdminInvariantPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture) : IAsyncLifetime
{
    private const string InitialPassword = "SeedAdmin1!";
    private const string ResetPassword = "SeedAdmin2!";
    private readonly string schemaName =
        $"single_admin_{Guid.NewGuid():N}";
    private string baseConnectionString = null!;
    private string schemaConnectionString = null!;

    public async Task InitializeAsync()
    {
        baseConnectionString = await fixture.GetConnectionStringAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var connection = new NpgsqlConnection(baseConnectionString);
        await connection.OpenAsync(timeout.Token);
        await using (var command = new NpgsqlCommand(
                         $"CREATE SCHEMA \"{schemaName}\";",
                         connection))
        {
            await command.ExecuteNonQueryAsync(timeout.Token);
        }

        schemaConnectionString = WithConnectionOptions(
            baseConnectionString,
            "single-admin-schema-migration");
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        await dbContext.Database.MigrateAsync(timeout.Token);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "AspNetUsers"
            ADD COLUMN IF NOT EXISTS "IsEnabled" boolean NOT NULL DEFAULT TRUE;
            """,
            timeout.Token);
    }

    public async Task DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = new NpgsqlConnection(baseConnectionString);
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;",
            connection);
        await command.ExecuteNonQueryAsync(timeout.Token);
    }

    [Fact]
    public async Task EmptyDatabase_FirstSeed_ShouldCreateOneCompleteAdmin()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);

        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-001",
                InitialPassword,
                "Original Admin"),
            budget.Token);

        var state = await ReadStateAsync(budget.Token);
        Assert.Equal(1, state.AdminCount);
        Assert.Equal(1, state.UserCount);
        Assert.Equal(1, state.EmployeeCount);
        Assert.NotNull(state.Admin);
        Assert.Equal(state.Admin!.AccountId, state.Admin.EmployeeId);
        Assert.Equal("ADMIN-001", state.Admin.IdentityEmployeeNo);
        Assert.Equal("ADMIN-001", state.Admin.EmployeeNo);
        Assert.Equal("Original Admin", state.Admin.RealName);
        Assert.True(state.Admin.IdentityEnabled);
        Assert.True(state.Admin.EmployeeActive);
        Assert.True(await PasswordMatchesAsync(
            state.Admin.AccountId,
            InitialPassword,
            budget.Token));
    }

    [Fact]
    public async Task RepeatedSeed_WithoutReset_ShouldSkipAccountAndNotRequirePassword()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-002",
                InitialPassword,
                "Stable Admin"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);

        await RunSeedAsync(
            new ConfigurationBuilder().Build(),
            budget.Token);

        var after = await ReadStateAsync(budget.Token);
        Assert.Equal(before, after);
        Assert.True(await PasswordMatchesAsync(
            before.Admin!.AccountId,
            InitialPassword,
            budget.Token));
    }

    [Fact]
    public async Task PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin()
    {
        using var budget = CreateBudget(TimeSpan.FromSeconds(45));
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-ACK",
                InitialPassword,
                "Commit Recovery Admin"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);
        await DisableAdminAsync(before.Admin!.AccountId, budget.Token);
        var interceptor = new ThrowOnceAfterCommitInterceptor();

        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-ACK",
                ResetPassword,
                "Ignored Name",
                resetPassword: true),
            budget.Token,
            interceptor);

        Assert.Equal(1, interceptor.ExceptionsThrown);
        var after = await ReadStateAsync(budget.Token);
        Assert.Equal(1, after.AdminCount);
        Assert.Equal(1, after.UserCount);
        Assert.Equal(1, after.EmployeeCount);
        Assert.Equal(before.Admin.AccountId, after.Admin!.AccountId);
        Assert.Equal("Commit Recovery Admin", after.Admin.RealName);
        Assert.True(after.Admin.IdentityEnabled);
        Assert.True(after.Admin.EmployeeActive);
        Assert.True(await PasswordMatchesAsync(
            after.Admin.AccountId,
            ResetPassword,
            budget.Token));
    }

    [Fact]
    public async Task ConcurrentSeeds_ShouldWaitOnAdvisoryLockAndCreateOneAdmin()
    {
        using var budget = CreateBudget(TimeSpan.FromSeconds(45));
        await ResetDataAsync(budget.Token);
        var firstApplication = $"single-admin-first-{Guid.NewGuid():N}";
        var secondApplication = $"single-admin-second-{Guid.NewGuid():N}";
        var pause = new PauseAfterAdminSeedLockInterceptor();
        await using var firstProvider = CreateProvider(
            WithConnectionOptions(
                baseConnectionString,
                firstApplication),
            pause);
        await using var secondProvider = CreateProvider(
            WithConnectionOptions(
                baseConnectionString,
                secondApplication));
        var configuration = CreateSeedConfiguration(
            "ADMIN-CONCURRENT",
            InitialPassword,
            "Concurrent Admin");

        var firstSeed = RunSeedWithProviderAsync(
            firstProvider,
            configuration,
            budget.Token);
        await pause.WaitUntilLockAcquiredAsync(budget.Token);
        var secondSeed = RunSeedWithProviderAsync(
            secondProvider,
            configuration,
            budget.Token);

        try
        {
            await WaitForAdvisoryLockContentionAsync(
                firstApplication,
                secondApplication,
                budget.Token);
        }
        finally
        {
            pause.Release();
        }

        await Task.WhenAll(firstSeed, secondSeed);
        var state = await ReadStateAsync(budget.Token);
        Assert.Equal(1, state.AdminCount);
        Assert.Equal(1, state.UserCount);
        Assert.Equal(1, state.EmployeeCount);
        Assert.Equal("ADMIN-CONCURRENT", state.Admin!.EmployeeNo);
    }

    [Fact]
    public async Task MatchingReset_ShouldPreserveIdAndRealNameAndReactivateAdmin()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-RESET",
                InitialPassword,
                "Original Name"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);
        await DisableAdminAsync(before.Admin!.AccountId, budget.Token);

        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-RESET",
                ResetPassword,
                "Ignored Replacement Name",
                resetPassword: true),
            budget.Token);

        var after = await ReadStateAsync(budget.Token);
        Assert.Equal(before.Admin.AccountId, after.Admin!.AccountId);
        Assert.Equal("Original Name", after.Admin.RealName);
        Assert.True(after.Admin.IdentityEnabled);
        Assert.True(after.Admin.EmployeeActive);
        Assert.False(await PasswordMatchesAsync(
            after.Admin.AccountId,
            InitialPassword,
            budget.Token));
        Assert.True(await PasswordMatchesAsync(
            after.Admin.AccountId,
            ResetPassword,
            budget.Token));
    }

    [Fact]
    public async Task ResetWithDifferentEmployeeNo_ShouldRejectWithoutCreatingSecondAdmin()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-ORIGINAL",
                InitialPassword,
                "Original Admin"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                CreateSeedConfiguration(
                    "ADMIN-OTHER",
                    ResetPassword,
                    "Other Admin",
                    resetPassword: true),
                budget.Token));

        Assert.Contains(
            "conflictType=SeedAdminNumberMismatch",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, await ReadStateAsync(budget.Token));
    }

    [Fact]
    public async Task MultipleAdmins_PreflightAndSeed_ShouldFailWithoutMutation()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        var firstId = await CreateManualAccountAsync(
            "ADMIN-MULTI-1",
            "First Admin",
            assignAdmin: true,
            budget.Token);
        var secondId = await CreateManualAccountAsync(
            "ADMIN-MULTI-2",
            "Second Admin",
            assignAdmin: true,
            budget.Token);
        var before = await ReadStateAsync(budget.Token);

        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var orchestrator = new DatabaseInitializationOrchestrator(
            services.GetRequiredService<IIoTDbContext>(),
            null!,
            null!,
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<DatabaseInitializationOrchestrator>.Instance);
        var preflight = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.EnsureIdentityAuthorizationPreflightAsync(
                budget.Token));
        var seed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                new ConfigurationBuilder().Build(),
                budget.Token));

        Assert.Contains(firstId.ToString(), preflight.Message, StringComparison.Ordinal);
        Assert.Contains(secondId.ToString(), preflight.Message, StringComparison.Ordinal);
        Assert.Contains("ADMIN-MULTI-1", preflight.Message, StringComparison.Ordinal);
        Assert.Contains("ADMIN-MULTI-2", preflight.Message, StringComparison.Ordinal);
        Assert.Contains(
            "conflictType=MigrationPreflight",
            preflight.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(InitialPassword, preflight.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", preflight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", preflight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "conflictType=SeedLockedPreflight",
            seed.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, await ReadStateAsync(budget.Token));
    }

    [Fact]
    public async Task ExistingOrdinaryTarget_ShouldRejectWithoutSilentElevation()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        var accountId = await CreateManualAccountAsync(
            "ORDINARY-001",
            "Ordinary Employee",
            assignAdmin: false,
            budget.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                CreateSeedConfiguration(
                    "ordinary-001",
                    InitialPassword,
                    "Attempted Admin"),
                budget.Token));

        Assert.Contains(
            "conflictType=TargetIdentityAlreadyExists",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "conflictType=TargetEmployeeAlreadyExists",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, (await ReadStateAsync(budget.Token)).AdminCount);
        Assert.Equal(
            0,
            await CountRolesAsync(SystemRoles.Admin, budget.Token));
        Assert.Equal(
            accountId,
            await FindUserIdAsync("ORDINARY-001", budget.Token));
    }

    [Fact]
    public async Task AdminWithoutEmployee_ShouldRejectAndNotAutoCreateEmployee()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-MISSING-EMPLOYEE",
                InitialPassword,
                "Missing Employee Admin"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);
        await ExecuteSqlAsync(
            $"DELETE FROM employees WHERE id = '{before.Admin!.AccountId}'::uuid;",
            budget.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                new ConfigurationBuilder().Build(),
                budget.Token));

        Assert.Contains(
            "conflictType=AdminEmployeeMissing",
            exception.Message,
            StringComparison.Ordinal);
        var after = await ReadStateAsync(budget.Token);
        Assert.Equal(1, after.AdminCount);
        Assert.Equal(0, after.EmployeeCount);
    }

    [Fact]
    public async Task AdminWithMismatchedEmployeeNo_ShouldRejectWithoutRewritingProfile()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-MISMATCH",
                InitialPassword,
                "Mismatch Admin"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);
        await ExecuteSqlAsync(
            $"""
             UPDATE employees
             SET employee_no = 'DIFFERENT-NO'
             WHERE id = '{before.Admin!.AccountId}'::uuid;
             """,
            budget.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                new ConfigurationBuilder().Build(),
                budget.Token));

        Assert.Contains(
            "conflictType=AdminEmployeeNumberMismatch",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            "DIFFERENT-NO",
            (await ReadStateAsync(budget.Token)).Admin!.EmployeeNo);
    }

    [Fact]
    public async Task DisabledAdmin_WithoutReset_ShouldFailWithOperationalHint()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        await RunSeedAsync(
            CreateSeedConfiguration(
                "ADMIN-DISABLED",
                InitialPassword,
                "Disabled Admin"),
            budget.Token);
        var before = await ReadStateAsync(budget.Token);
        await DisableAdminAsync(before.Admin!.AccountId, budget.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                new ConfigurationBuilder().Build(),
                budget.Token));

        Assert.Contains(
            "conflictType=AdminDisabledResetRequired",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            SeedAdminOptions.ResetPasswordKey,
            exception.Message,
            StringComparison.Ordinal);
        var after = await ReadStateAsync(budget.Token);
        Assert.False(after.Admin!.IdentityEnabled);
        Assert.False(after.Admin.EmployeeActive);
    }

    [Fact]
    public async Task SeedFailureAfterIdentityWrites_ShouldRollbackAllSeedData()
    {
        using var budget = CreateBudget();
        await ResetDataAsync(budget.Token);
        var failure = new ThrowOnEmployeeInsertInterceptor();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(
                CreateSeedConfiguration(
                    "ADMIN-ROLLBACK",
                    InitialPassword,
                    "Rollback Admin"),
                budget.Token,
                failure));

        Assert.Contains(
            "simulated employee persistence failure",
            exception.Message,
            StringComparison.Ordinal);
        var state = await ReadStateAsync(budget.Token);
        Assert.Equal(0, state.AdminCount);
        Assert.Equal(0, state.UserCount);
        Assert.Equal(0, state.EmployeeCount);
        Assert.Equal(0, state.RoleCount);
        Assert.Equal(0, await CountOutboxAsync(budget.Token));
    }

    private CancellationTokenSource CreateBudget(TimeSpan? timeout = null) =>
        new(timeout ?? TimeSpan.FromSeconds(30));

    private async Task ResetDataAsync(CancellationToken cancellationToken)
    {
        const string sql =
            """
            DO $$
            DECLARE
                table_list text;
            BEGIN
                SELECT string_agg(
                    format('%I.%I', schemaname, tablename),
                    ', ')
                INTO table_list
                FROM pg_tables
                WHERE schemaname = current_schema()
                  AND tablename <> '__EFMigrationsHistory';

                IF table_list IS NOT NULL THEN
                    EXECUTE 'TRUNCATE TABLE '
                        || table_list
                        || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            """;
        await ExecuteSqlAsync(sql, cancellationToken);
    }

    private async Task RunSeedAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken,
        IInterceptor? interceptor = null)
    {
        await using var provider = CreateProvider(
            schemaConnectionString,
            interceptor);
        await RunSeedWithProviderAsync(
            provider,
            configuration,
            cancellationToken);
    }

    private static async Task RunSeedWithProviderAsync(
        ServiceProvider provider,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await SystemInitData.SeedAsync(
            services.GetRequiredService<IIoTDbContext>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            configuration,
            cancellationToken);
    }

    private async Task<Guid> CreateManualAccountAsync(
        string employeeNo,
        string realName,
        bool assignAdmin,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var userManager =
            services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager =
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (assignAdmin
            && await roleManager.FindByNameAsync(SystemRoles.Admin) is null)
        {
            Assert.True((await roleManager.CreateAsync(
                new IdentityRole<Guid>(SystemRoles.Admin))).Succeeded);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = employeeNo,
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(
            user,
            InitialPassword)).Succeeded);
        if (assignAdmin)
        {
            Assert.True((await userManager.AddToRoleAsync(
                user,
                SystemRoles.Admin)).Succeeded);
        }

        dbContext.Employees.Add(new Employee(user.Id, employeeNo, realName));
        await dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    private async Task DisableAdminAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var userManager =
            services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(accountId.ToString())
                   ?? throw new InvalidOperationException(
                       "Expected seeded admin identity.");
        user.IsEnabled = false;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        var employee = await dbContext.Employees.SingleAsync(
            candidate => candidate.Id == accountId,
            cancellationToken);
        employee.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<SeedDatabaseState> ReadStateAsync(
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var admins = await (
                from userRole in dbContext.UserRoles.AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                join user in dbContext.Users.AsNoTracking()
                    on userRole.UserId equals user.Id
                where role.Name == SystemRoles.Admin
                orderby user.Id
                select new
                {
                    user.Id,
                    user.UserName,
                    user.IsEnabled
                })
            .ToArrayAsync(cancellationToken);
        SeededAdminState? admin = null;
        if (admins.Length == 1)
        {
            var identity = admins[0];
            var employee = await dbContext.Employees
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == identity.Id,
                    cancellationToken);
            admin = new SeededAdminState(
                identity.Id,
                identity.UserName,
                identity.IsEnabled,
                employee?.Id,
                employee?.EmployeeNo,
                employee?.RealName,
                employee?.IsActive);
        }

        return new SeedDatabaseState(
            admins.Length,
            await dbContext.Users.CountAsync(cancellationToken),
            await dbContext.Employees.CountAsync(cancellationToken),
            await dbContext.Roles.CountAsync(cancellationToken),
            admin);
    }

    private async Task<bool> PasswordMatchesAsync(
        Guid accountId,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(accountId.ToString())
                   ?? throw new InvalidOperationException(
                       "Expected seeded admin identity.");
        return await userManager.CheckPasswordAsync(user, password);
    }

    private async Task<int> CountRolesAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        return await dbContext.Roles.CountAsync(
            role => role.Name == roleName,
            cancellationToken);
    }

    private async Task<Guid> FindUserIdAsync(
        string employeeNo,
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(employeeNo)
                   ?? throw new InvalidOperationException(
                       "Expected identity user.");
        cancellationToken.ThrowIfCancellationRequested();
        return user.Id;
    }

    private async Task<int> CountOutboxAsync(
        CancellationToken cancellationToken)
    {
        await using var provider = CreateProvider(schemaConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        return await dbContext.OutboxMessages.CountAsync(cancellationToken);
    }

    private async Task ExecuteSqlAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(schemaConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WaitForAdvisoryLockContentionAsync(
        string firstApplication,
        string secondApplication,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var connection =
            new NpgsqlConnection(schemaConnectionString);
        await connection.OpenAsync(timeout.Token);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));

        while (await timer.WaitForNextTickAsync(timeout.Token))
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM pg_locks lock
                        INNER JOIN pg_stat_activity activity
                            ON activity.pid = lock.pid
                        WHERE lock.locktype = 'advisory'
                          AND lock.granted
                          AND activity.application_name = @first_application
                    ),
                    EXISTS (
                        SELECT 1
                        FROM pg_locks lock
                        INNER JOIN pg_stat_activity activity
                            ON activity.pid = lock.pid
                        WHERE lock.locktype = 'advisory'
                          AND NOT lock.granted
                          AND activity.application_name = @second_application
                    );
                """,
                connection);
            command.Parameters.AddWithValue(
                "first_application",
                firstApplication);
            command.Parameters.AddWithValue(
                "second_application",
                secondApplication);
            await using var reader =
                await command.ExecuteReaderAsync(timeout.Token);
            Assert.True(await reader.ReadAsync(timeout.Token));
            if (reader.GetBoolean(0) && reader.GetBoolean(1))
            {
                return;
            }
        }

        throw new TimeoutException(
            "Concurrent seed did not reach the bounded advisory-lock wait.");
    }

    private string WithConnectionOptions(
        string connectionString,
        string applicationName)
    {
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schemaName,
            ApplicationName = applicationName
        }.ConnectionString;
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        IInterceptor? interceptor = null)
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
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
        });
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();
        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateSeedConfiguration(
        string employeeNo,
        string password,
        string realName,
        bool resetPassword = false)
    {
        var values = new Dictionary<string, string?>
        {
            [SeedAdminOptions.EmployeeNoKey] = employeeNo,
            [SeedAdminOptions.PasswordKey] = password,
            [SeedAdminOptions.RealNameKey] = realName
        };
        if (resetPassword)
        {
            values[SeedAdminOptions.ResetPasswordKey] = "true";
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class PauseAfterAdminSeedLockInterceptor
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource acquired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;

        public Task WaitUntilLockAcquiredAsync(
            CancellationToken cancellationToken) =>
            acquired.Task.WaitAsync(cancellationToken);

        public void Release() => release.TrySetResult();

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "pg_advisory_xact_lock",
                    StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref paused, 1, 0) == 0)
            {
                acquired.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class ThrowOnEmployeeInsertInterceptor
        : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                .Entries<Employee>()
                .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException(
                    "simulated employee persistence failure");
            }

            return ValueTask.FromResult(result);
        }
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
                    "simulated seed commit confirmation loss",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.SerializationFailure);
            }

            return Task.CompletedTask;
        }
    }

    private sealed record SeedDatabaseState(
        int AdminCount,
        int UserCount,
        int EmployeeCount,
        int RoleCount,
        SeededAdminState? Admin);

    private sealed record SeededAdminState(
        Guid AccountId,
        string? IdentityEmployeeNo,
        bool IdentityEnabled,
        Guid? EmployeeId,
        string? EmployeeNo,
        string? RealName,
        bool? EmployeeActive);
}
