using FluentValidation;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.EntityFrameworkCore.Uploads;
using IIoT.EventBus;
using IIoT.Infrastructure.Authentication;
using IIoT.IdentityService.Commands;
using IIoT.Infrastructure.Logging;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.SharedKernel.Architecture;
using IIoT.SharedKernel.Configuration;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace IIoT.CloudPlatform.ArchitectureTests;

public sealed class PersistenceBoundaryArchitectureTests
{
    [Fact]
    public void ProductionPersistenceWriteEntrypoints_ShouldBeDynamicallyClassified()
    {
        var inventory = PersistenceWriteInventory.DiscoverProduction();

        Assert.NotEmpty(inventory.Entries);
        Assert.True(
            inventory.UnresolvedCandidates.Count == 0,
            "Persistence calls that could not be resolved fail closed:\n" +
            string.Join("\n", inventory.UnresolvedCandidates));
        Assert.True(
            inventory.UnclassifiedEntries.Count == 0,
            "Unclassified production persistence write entrypoints:\n" +
            string.Join("\n", inventory.UnclassifiedEntries.Select(entry => entry.Diagnostic)));
        var entriesWithoutConcreteEvidence = inventory.Entries
            .Where(entry => entry.Evidence.RelativePath.Contains(
                "ArchitectureTests",
                StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            entriesWithoutConcreteEvidence.Length == 0,
            "Production persistence entries must bind concrete behavior evidence:\n" +
            string.Join(
                "\n",
                entriesWithoutConcreteEvidence.Select(entry => entry.Diagnostic)));
        Assert.All(
            inventory.Entries,
            entry => PersistenceWriteInventory.AssertEvidenceExists(entry.Evidence));

        Assert.False(File.Exists(CloudRepositoryPath.Find(
            "src", "services", "IIoT.Services.Contracts", "Contracts", "Messaging",
            "IIntegrationEventOutbox.cs")));
        Assert.False(File.Exists(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "Outbox",
            "EfIntegrationEventOutbox.cs")));
    }

    [Fact]
    public void PersistenceInventory_ShouldRejectUnprotectedEfWrites()
    {
        const string source =
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class UnsafeWriter(DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => context.SaveChangesAsync(cancellationToken);
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        var entry = Assert.Single(inventory.UnclassifiedEntries);
        Assert.Contains("ef-save", entry.SinkKinds);
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldDiscoverUnitOfWorkTransactionReceiverItself()
    {
        const string source =
            """
            using IIoT.Services.Contracts.Persistence;

            public sealed class UnsafeHostWriter(IUnitOfWork unitOfWork)
            {
                public Task WriteAsync(CancellationToken cancellationToken)
                    => unitOfWork.BeginTransactionAsync(cancellationToken);
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(
            source,
            "src/hosts/IIoT.HttpApi/UnsafeHostWriter.cs");

        var entry = Assert.Single(inventory.UnclassifiedEntries);
        Assert.Contains("manual-transaction", entry.SinkKinds);
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldClassifyWritesInsideResolvedReplayCallback()
    {
        const string source =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;

            public sealed class SafeWriter(IUnitOfWork unitOfWork, DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => unitOfWork.ExecuteResilientAsync(
                        callbackToken => context.SaveChangesAsync(callbackToken),
                        cancellationToken);
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).Entries);

        Assert.Equal(
            PersistenceWriteClassification.ExecutionStrategyReplayRoot,
            entry.Classification);
    }

    [Fact]
    public void PersistenceInventory_ShouldTrackWriterCallersAcrossProjectGraph()
    {
        const string writerSource =
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class ExternallyCallableWriter(DbContext context)
            {
                public Task<int> PersistAsync(CancellationToken cancellationToken)
                    => context.SaveChangesAsync(cancellationToken);
            }
            """;
        const string protectedCallerSource =
            """
            using IIoT.Services.Contracts.Persistence;

            public sealed class LocalProtectedCaller(
                IUnitOfWork unitOfWork,
                ExternallyCallableWriter writer)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => unitOfWork.ExecuteResilientAsync(
                        writer.PersistAsync,
                        cancellationToken);
            }
            """;
        const string unprotectedCallerSource =
            """
            public sealed class ExternalUnprotectedCaller(ExternallyCallableWriter writer)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => writer.PersistAsync(cancellationToken);
            }
            """;

        var protectedOnly = PersistenceWriteInventory.DiscoverProjectGraphSnippets(
            writerSource,
            protectedCallerSource);
        var protectedEntry = Assert.Single(protectedOnly.Entries);
        Assert.Equal(
            PersistenceWriteClassification.ExecutionStrategyReplayRoot,
            protectedEntry.Classification);
        Assert.Empty(protectedOnly.UnresolvedCandidates);

        var mixedCallers = PersistenceWriteInventory.DiscoverProjectGraphSnippets(
            writerSource,
            protectedCallerSource,
            unprotectedCallerSource);
        var entry = Assert.Single(
            mixedCallers.UnclassifiedEntries);

        Assert.Contains("ef-save", entry.SinkKinds);
        Assert.Contains("ExternallyCallableWriter.PersistAsync", entry.Method);
        Assert.Empty(mixedCallers.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldVerifyUnitOfWorkReplayImplementationBody()
    {
        const string safeImplementation =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Storage;

            public sealed class SafeUnitOfWork(IExecutionStrategy strategy) : IUnitOfWork
            {
                public Task<TResult> ExecuteResilientAsync<TResult>(
                    Func<CancellationToken, Task<TResult>> operation,
                    CancellationToken cancellationToken = default)
                    => strategy.ExecuteAsync(operation, cancellationToken);

                public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
                public Task CommitAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
                public Task RollbackAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;
        const string unsafeImplementation =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Storage;

            public sealed class UnsafeUnitOfWork(IExecutionStrategy strategy) : IUnitOfWork
            {
                public async Task<TResult> ExecuteResilientAsync<TResult>(
                    Func<CancellationToken, Task<TResult>> operation,
                    CancellationToken cancellationToken = default)
                {
                    await strategy.ExecuteAsync(
                        static _ => Task.CompletedTask,
                        cancellationToken);
                    return await operation(cancellationToken);
                }

                public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
                public Task CommitAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
                public Task RollbackAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;
        const string escapedAliasImplementation =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Storage;

            public sealed class EscapedAliasUnitOfWork(IExecutionStrategy strategy) : IUnitOfWork
            {
                public async Task<TResult> ExecuteResilientAsync<TResult>(
                    Func<CancellationToken, Task<TResult>> operation,
                    CancellationToken cancellationToken = default)
                {
                    var escaped = operation;
                    _ = await strategy.ExecuteAsync(operation, cancellationToken);
                    return await escaped(cancellationToken);
                }

                public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
                public Task CommitAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
                public Task RollbackAsync(CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;
        const string caller =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;

            public sealed class Writer(IUnitOfWork unitOfWork, DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => unitOfWork.ExecuteResilientAsync(
                        callbackToken => context.SaveChangesAsync(callbackToken),
                        cancellationToken);
            }
            """;

        Assert.True(
            PersistenceWriteInventory.VerifyUnitOfWorkReplayImplementationSnippet(
                safeImplementation));
        Assert.False(
            PersistenceWriteInventory.VerifyUnitOfWorkReplayImplementationSnippet(
                unsafeImplementation));
        Assert.False(
            PersistenceWriteInventory.VerifyUnitOfWorkReplayImplementationSnippet(
                escapedAliasImplementation));
        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(
                caller,
                unitOfWorkReplayContractVerified: false)
                .UnclassifiedEntries);
        Assert.Contains("ef-save", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldRejectNamedHelperWithoutExecutionStrategy()
    {
        const string source =
            """
            using Microsoft.EntityFrameworkCore;

            namespace IIoT.EntityFrameworkCore.Identity;

            public sealed class IdentityPasswordService(DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => ExecuteRecoverableAsync(
                        callbackToken => context.SaveChangesAsync(callbackToken),
                        cancellationToken);

                private static Task<int> ExecuteRecoverableAsync(
                    Func<CancellationToken, Task<int>> attempt,
                    CancellationToken cancellationToken)
                    => attempt(cancellationToken);
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("ef-save", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldRejectNamedHelperThatDoesNotRouteItsAttempt()
    {
        const string source =
            """
            using Microsoft.EntityFrameworkCore;

            namespace IIoT.EntityFrameworkCore.Identity;

            public sealed class IdentityPasswordService(DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => ExecuteRecoverableAsync(
                        callbackToken => context.SaveChangesAsync(callbackToken),
                        cancellationToken);

                private async Task<int> ExecuteRecoverableAsync(
                    Func<CancellationToken, Task<int>> attempt,
                    CancellationToken cancellationToken)
                {
                    var strategy = context.Database.CreateExecutionStrategy();
                    _ = await strategy.ExecuteAsync(
                        static _ => Task.FromResult(0),
                        cancellationToken);
                    return await attempt(cancellationToken);
                }
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("ef-save", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldDiscoverResolvedIdentityMutationsFromManagerAndStoreContracts()
    {
        const string source =
            """
            using IIoT.EntityFrameworkCore.Identity;
            using Microsoft.AspNetCore.Identity;

            public sealed class UnsafeIdentityWriter(
                UserManager<ApplicationUser> userManager,
                IUserLockoutStore<ApplicationUser> lockoutStore)
            {
                public Task<IdentityResult> SetLockoutAsync(ApplicationUser user)
                    => userManager.SetLockoutEndDateAsync(
                        user,
                        DateTimeOffset.UtcNow.AddMinutes(5));

                public Task<IdentityResult> RefreshStampAsync(ApplicationUser user)
                    => userManager.UpdateSecurityStampAsync(user);

                public Task SetStoreLockoutAsync(
                    ApplicationUser user,
                    CancellationToken cancellationToken)
                    => lockoutStore.SetLockoutEndDateAsync(
                        user,
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        cancellationToken);
            }
            """;

        var entries = PersistenceWriteInventory.DiscoverSnippet(source)
            .UnclassifiedEntries;

        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.Contains("identity-write", entry.SinkKinds));
    }

    [Fact]
    public void PersistenceInventory_ShouldTreatPasswordRehashCheckAsIdentityWrite()
    {
        const string source =
            """
            using IIoT.EntityFrameworkCore.Identity;
            using Microsoft.AspNetCore.Identity;

            public sealed class UnsafePasswordCheck(
                UserManager<ApplicationUser> userManager)
            {
                public Task<bool> CheckAsync(ApplicationUser user, string password)
                    => userManager.CheckPasswordAsync(user, password);
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("identity-write", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldFailClosedForUnknownIdentityManagerMethods()
    {
        const string source =
            """
            using IIoT.EntityFrameworkCore.Identity;
            using Microsoft.AspNetCore.Identity;

            public sealed class FutureIdentityWriter(
                UserManager<ApplicationUser> userManager)
            {
                public Task<IdentityResult> WriteAsync(ApplicationUser user)
                    => userManager.FutureMutationAsync(user);
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        Assert.Empty(inventory.Entries);
        Assert.Contains(
            inventory.UnresolvedCandidates,
            candidate => candidate.Contains(
                "FutureMutationAsync",
                StringComparison.Ordinal));
    }

    [Fact]
    public void PersistenceInventory_ShouldRequireExactCallableEvidenceBinding()
    {
        const string source =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;

            namespace IIoT.EmployeeService.Commands.Employees;

            public sealed class NewlyAddedWriter(
                IUnitOfWork unitOfWork,
                DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => unitOfWork.ExecuteResilientAsync(
                        callbackToken => context.SaveChangesAsync(callbackToken),
                        cancellationToken);
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(
                source,
                "src/services/IIoT.EmployeeService/Commands/Human/Employees/NewlyAddedWriter.cs")
                .Entries);

        Assert.Equal(
            PersistenceWriteClassification.ExecutionStrategyReplayRoot,
            entry.Classification);
        Assert.Contains("ArchitectureTests", entry.Evidence.RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceInventory_ShouldRejectWritesEvaluatedAsReplayState()
    {
        const string source =
            """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Storage;

            public sealed class StateWriter(
                IExecutionStrategy strategy,
                DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                    => strategy.ExecuteAsync(
                        context.SaveChangesAsync(cancellationToken),
                        static (pendingSave, _) => pendingSave,
                        cancellationToken);
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("ef-save", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldRejectAliasedPersistenceMethodGroups()
    {
        const string source =
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class AliasedWriter(DbContext context)
            {
                public Task<int> WriteAsync(CancellationToken cancellationToken)
                {
                    Func<CancellationToken, Task<int>> save = context.SaveChangesAsync;
                    return save(cancellationToken);
                }
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("ef-save-method-group", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldRejectMethodGroupsEscapingReplayCallbacks()
    {
        const string source =
            """
            using IIoT.Services.Contracts.Persistence;
            using Microsoft.EntityFrameworkCore;

            public sealed class DeferredWriter(DbContext context)
            {
                public Task<int> PersistAsync(CancellationToken cancellationToken)
                    => context.SaveChangesAsync(cancellationToken);
            }

            public sealed class ReplayFactory(
                IUnitOfWork unitOfWork,
                DeferredWriter writer)
            {
                public Task<Func<CancellationToken, Task<int>>> CreateAsync(
                    CancellationToken cancellationToken)
                    => unitOfWork.ExecuteResilientAsync(
                        _ => Task.FromResult<Func<CancellationToken, Task<int>>>(
                            writer.PersistAsync),
                        cancellationToken);
            }

            public sealed class OutsideCaller(ReplayFactory factory)
            {
                public async Task<int> WriteAsync(
                    CancellationToken cancellationToken)
                {
                    var write = await factory.CreateAsync(cancellationToken);
                    return await write(cancellationToken);
                }
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("ef-save", entry.SinkKinds);
        Assert.Contains("DeferredWriter.PersistAsync", entry.Method);
    }

    [Fact]
    public void PersistenceInventory_ShouldUseSymbolsInsteadOfCommentsOrSafeLanguageConstructs()
    {
        const string source =
            """
            using IIoT.SharedKernel.Result;

            public sealed class SafeValue
            {
                // SaveChangesAsync ExecuteSqlRawAsync BeginTransactionAsync
                public SafeValue() { }

                public string Name { get; } = "SaveChangesAsync";

                public Result Read() => Result.Success();
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        Assert.Empty(inventory.Entries);
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldFailClosedWhenPersistenceSymbolCannotResolve()
    {
        const string source =
            """
            public sealed class UnknownWriter
            {
                public async Task WriteAsync(dynamic context)
                {
                    await context.SaveChangesAsync();
                }
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        Assert.Empty(inventory.Entries);
        Assert.Single(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldFailClosedWhenDapperExtensionCannotResolve()
    {
        const string source =
            """
            using System.Data;

            public sealed class UnknownDapperWriter(IDbConnection connection)
            {
                public Task WriteAsync()
                    => connection.ExecuteAsync("insert into sample(id) values (1)");
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        Assert.Empty(inventory.Entries);
        Assert.Single(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldDiscoverDapperResultReturningWrites()
    {
        const string source =
            """
            using System.Data;
            using Dapper;

            public sealed class ReturningDapperWriter(IDbConnection connection)
            {
                public async Task WriteAsync(CancellationToken cancellationToken)
                {
                    _ = await connection.QuerySingleAsync<int>(
                        "delete from sample returning id");
                    _ = await connection.ExecuteScalarAsync<int>(
                        "update sample set value = 1 returning value");
                    using var reader = await connection.ExecuteReaderAsync(
                        "insert into sample(value) values (1) returning value");
                }
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);
        var entry = Assert.Single(inventory.UnclassifiedEntries);

        Assert.Equal(["dapper-write"], entry.SinkKinds);
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldNotTrustReadOnlyPortForConstantDapperDml()
    {
        const string source =
            """
            using System.Data;
            using Dapper;
            using IIoT.SharedKernel.Architecture;

            public interface IUnsafeQueryPort : IReadOnlyQueryPort
            {
                Task<int> ReadAsync(CancellationToken cancellationToken);
            }

            public sealed class UnsafeQueryPort(IDbConnection connection) : IUnsafeQueryPort
            {
                public async Task<int> ReadAsync(CancellationToken cancellationToken)
                {
                    var command = new CommandDefinition(
                        "delete from sample returning id",
                        cancellationToken: cancellationToken);
                    return await connection.QuerySingleAsync<int>(command);
                }
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("dapper-write", entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldNotTrustReadOnlyPortForUnresolvedDapperSql()
    {
        const string source =
            """
            using System.Data;
            using Dapper;
            using IIoT.SharedKernel.Architecture;

            public interface IUnsafeQueryPort : IReadOnlyQueryPort
            {
                Task<int> ReadAsync(string sql, CancellationToken cancellationToken);
            }

            public sealed class UnsafeQueryPort(IDbConnection connection) : IUnsafeQueryPort
            {
                public async Task<int> ReadAsync(
                    string sql,
                    CancellationToken cancellationToken)
                {
                    var command = new CommandDefinition(
                        sql,
                        cancellationToken: cancellationToken);
                    return await connection.QuerySingleAsync<int>(command);
                }
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Contains("dapper-write", entry.SinkKinds);
    }

    [Theory]
    [InlineData("delete from sample returning id")]
    [InlineData("select dangerous_side_effect()")]
    [InlineData("select lower(value::text) from sample")]
    [InlineData("select public.lower(value::text) from sample")]
    [InlineData("select pg_catalog.upper(value::integer) from sample")]
    [InlineData("select pg_catalog.sum(value::bigint) from sample")]
    [InlineData("select 1; delete from sample")]
    public void ReadOnlyCommandDefinition_ShouldRejectUnprovenSql(string sql)
    {
        var constructor = GetReadOnlyCommandConstructor();

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => constructor.Invoke(
            [
                sql,
                null,
                null,
                null,
                null,
                global::Dapper.CommandFlags.Buffered,
                CancellationToken.None
            ]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void ReadOnlyCommandDefinition_ShouldRejectStoredProcedures()
    {
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => GetReadOnlyCommandConstructor().Invoke(
            [
                "select_apply",
                null,
                null,
                null,
                System.Data.CommandType.StoredProcedure,
                global::Dapper.CommandFlags.Buffered,
                CancellationToken.None
            ]));

        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void ReadOnlyCommandDefinition_ShouldAcceptProvenSql()
    {
        var command = GetReadOnlyCommandConstructor().Invoke(
        [
            "select 1",
            null,
            null,
            null,
            null,
            global::Dapper.CommandFlags.Buffered,
            CancellationToken.None
        ]);

        Assert.NotNull(command);
    }

    [Theory]
    [InlineData("select 1")]
    [InlineData("with sample as (select 1) select * from sample")]
    [InlineData("select * from sample where id = any(@Ids)")]
    [InlineData("select pg_catalog.count(*) from sample")]
    [InlineData("select pg_catalog.upper(name::text) from sample")]
    [InlineData("select pg_catalog.min(name::text) from sample")]
    [InlineData("select pg_catalog.sum(value::integer) from sample")]
    [InlineData("select pg_catalog.max(seen_at::timestamptz) from sample")]
    [InlineData("select pg_catalog.make_interval(hours => hour::integer, mins => minute::integer) from sample")]
    public void ReadOnlySqlGuard_ShouldAcceptProvenReadOnlySql(string sql)
    {
        Assert.Equal(sql, ReadOnlySqlGuard.Require(sql));
    }

    private static System.Reflection.ConstructorInfo GetReadOnlyCommandConstructor()
    {
        var wrapperType = typeof(IIoT.Dapper.DependencyInjection).Assembly.GetType(
            "IIoT.Dapper.ReadOnlyCommandDefinition");
        Assert.NotNull(wrapperType);
        return Assert.Single(wrapperType.GetConstructors());
    }

    [Fact]
    public void PersistenceInventory_ShouldDiscoverOpenIddictManagerMutations()
    {
        const string source =
            """
            using OpenIddict.Abstractions;

            public sealed class UnsafeOpenIddictWriter(
                IOpenIddictAuthorizationManager authorizationManager)
            {
                public ValueTask<long> PruneAsync(CancellationToken cancellationToken)
                    => authorizationManager.PruneAsync(
                        DateTimeOffset.UtcNow,
                        cancellationToken);

                public ValueTask<bool> RevokeAsync(
                    object authorization,
                    CancellationToken cancellationToken)
                    => authorizationManager.TryRevokeAsync(
                        authorization,
                        cancellationToken);

                public IAsyncEnumerable<object> ReadAsync(
                    CancellationToken cancellationToken)
                    => authorizationManager.FindBySubjectAsync(
                        "subject",
                        cancellationToken);
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        Assert.Equal(2, inventory.UnclassifiedEntries.Count);
        Assert.All(
            inventory.UnclassifiedEntries,
            entry => Assert.Contains("oidc-write", entry.SinkKinds));
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldFailClosedForUnknownOpenIddictManagerMethods()
    {
        const string source =
            """
            using OpenIddict.Abstractions;

            public sealed class FutureOpenIddictWriter(
                IOpenIddictAuthorizationManager authorizationManager)
            {
                public Task WriteAsync(CancellationToken cancellationToken)
                    => authorizationManager.FutureMutationAsync(cancellationToken);
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);

        Assert.Empty(inventory.Entries);
        Assert.Contains(
            inventory.UnresolvedCandidates,
            candidate => candidate.Contains(
                "FutureMutationAsync",
                StringComparison.Ordinal));
    }

    [Fact]
    public void PersistenceInventory_ShouldDiscoverDapperAndDbCommandWritesBySymbol()
    {
        const string source =
            """
            using System.Data.Common;
            using Dapper;

            public sealed class SqlWriter(DbConnection connection, DbCommand command)
            {
                public async Task WriteAsync(CancellationToken cancellationToken)
                {
                    await connection.ExecuteAsync("insert into sample(id) values (1)");
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            """;

        var entry = Assert.Single(
            PersistenceWriteInventory.DiscoverSnippet(source).UnclassifiedEntries);

        Assert.Equal(
            ["dapper-write", "db-command-write"],
            entry.SinkKinds);
    }

    [Fact]
    public void PersistenceInventory_ShouldTreatDbCommandScalarAndReaderAsPotentialWrites()
    {
        const string source =
            """
            using System.Data.Common;

            public sealed class ReturningSqlWriter(DbCommand command)
            {
                public async Task WriteAsync(CancellationToken cancellationToken)
                {
                    _ = await command.ExecuteScalarAsync(cancellationToken);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                }
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);
        var entry = Assert.Single(inventory.UnclassifiedEntries);

        Assert.Equal(["db-command-write"], entry.SinkKinds);
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Fact]
    public void PersistenceInventory_ShouldTreatDbBatchExecutionsAsPotentialWrites()
    {
        const string source =
            """
            using System.Data.Common;

            public sealed class BatchWriter(DbBatch batch)
            {
                public async Task WriteAsync(CancellationToken cancellationToken)
                {
                    _ = await batch.ExecuteNonQueryAsync(cancellationToken);
                    _ = await batch.ExecuteScalarAsync(cancellationToken);
                    await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
                }
            }
            """;

        var inventory = PersistenceWriteInventory.DiscoverSnippet(source);
        var entry = Assert.Single(inventory.UnclassifiedEntries);

        Assert.Equal(["db-command-write"], entry.SinkKinds);
        Assert.Empty(inventory.UnresolvedCandidates);
    }

    [Theory]
    [InlineData("src/tests/FakeWrite.cs")]
    [InlineData("src/testing/FakeWrite.cs")]
    [InlineData("src/analyzers/FakeWrite.cs")]
    [InlineData("src/services/Fake/obj/Release/Fake.g.cs")]
    [InlineData("src/infrastructure/Fake/Migrations/20260731_Fake.cs")]
    [InlineData("src/infrastructure/Fake/Fake.Designer.cs")]
    public void PersistenceInventory_ShouldExcludeTestsAndGeneratedSources(string path)
    {
        Assert.False(PersistenceWriteInventory.IsIncludedProductionSource(path));
    }

    [Fact]
    public void PersistenceInventory_ShouldIncludeOrdinaryProductionSources()
    {
        Assert.True(PersistenceWriteInventory.IsIncludedProductionSource(
            "src/services/IIoT.ProductionService/Commands/Write.cs"));
    }

    [Fact]
    public void DeviceCascadeDeletion_ShouldStayOnEfCoreWritePath()
    {
        var implementationSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "QueryServices",
            "EfDeviceDeletionDependencyService.cs"));
        var registrationSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "DependencyInjection.cs"));
        var refreshTokenSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "Identity",
            "EfRefreshTokenService.cs"));

        Assert.True(typeof(IDeviceDeletionDependencyQueryService).IsAssignableFrom(
            typeof(IIoT.EntityFrameworkCore.QueryServices.EfDeviceDeletionDependencyService)));
        Assert.DoesNotContain(
            typeof(IIoT.Dapper.DependencyInjection).Assembly.GetTypes(),
            type => !type.IsAbstract && typeof(IDeviceDeletionDependencyQueryService).IsAssignableFrom(type));
        Assert.Contains(
            "AddScoped<IDeviceDeletionDependencyQueryService, QueryServices.EfDeviceDeletionDependencyService>()",
            registrationSource,
            StringComparison.Ordinal);

        Assert.Contains("SqlQuery<DeviceDeletionImpactRow>", implementationSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteSqlInterpolatedAsync", implementationSource, StringComparison.Ordinal);
        Assert.Contains("CreateExecutionStrategy()", implementationSource, StringComparison.Ordinal);
        Assert.Contains(
            "DeviceDeletionTransactionLock.AcquireAsync",
            implementationSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "DeviceDeletionTransactionLock.AcquireAsync",
            refreshTokenSource,
            StringComparison.Ordinal);
        var refreshTokenRoot = CSharpSyntaxTree
            .ParseText(refreshTokenSource)
            .GetRoot();
        var protectedRefreshTokenMethods = refreshTokenRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                invocation.Expression.ToString()
                    == "DeviceDeletionTransactionLock.AcquireAsync")
            .Select(invocation => invocation
                .Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .First()
                .Identifier
                .ValueText)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "IssueAttemptAsync",
                "RevokeSubjectAttemptAsync",
                "RotateAttemptAsync"
            ],
            protectedRefreshTokenMethods);
        Assert.True(
            implementationSource.IndexOf(
                "CreateExecutionStrategy()",
                StringComparison.Ordinal)
            < implementationSource.IndexOf(
                "BeginTransactionAsync(",
                StringComparison.Ordinal));
        Assert.DoesNotContain("CountTableAsync", implementationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteDeleteAsync", implementationSource, StringComparison.Ordinal);
        foreach (var table in new[]
                 {
                     "recipes", "hourly_capacity", "device_logs", "pass_station_records",
                     "edge_device_client_states", "edge_device_client_version_snapshots",
                     "edge_device_client_plugin_versions", "edge_device_runtime_heartbeats",
                     "upload_receive_registrations", "employee_device_accesses",
                     "refresh_token_sessions", "edge_host_plc_runtime_states"
                 })
        {
            Assert.Contains(table, implementationSource, StringComparison.Ordinal);
        }

        Assert.Contains("\"ActorType\"", implementationSource, StringComparison.Ordinal);
        Assert.Contains("\"SubjectId\"", implementationSource, StringComparison.Ordinal);
        var deleteSectionStart = implementationSource.IndexOf(
            "private async Task DeleteAssociatedRowsAsync",
            StringComparison.Ordinal);
        var deleteSectionEnd = implementationSource.IndexOf(
            "public sealed class DeviceDeletionImpactRow",
            StringComparison.Ordinal);
        Assert.True(deleteSectionStart >= 0 && deleteSectionEnd > deleteSectionStart);
        var deleteSection = implementationSource[deleteSectionStart..deleteSectionEnd];
        Assert.Contains("delete from edge_host_plc_runtime_states", deleteSection, StringComparison.Ordinal);
        Assert.DoesNotContain("delete from edge_hosts", deleteSection, StringComparison.Ordinal);
        Assert.DoesNotContain("delete from edge_host_plc_bindings", deleteSection, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessHandlers_ShouldNotOpenManualTransactionsOutsideResilientBoundary()
    {
        var serviceRoot = CloudRepositoryPath.Find("src", "services");
        var violations = Directory
            .GetFiles(serviceRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => FindManualTransactionViolations(
                File.ReadAllText(path),
                Path.GetRelativePath(serviceRoot, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Manual transactions outside resilient execution strategy: {string.Join(", ", violations)}");
    }

    [Fact]
    public void ManualTransactionGuard_ShouldInspectEveryTransactionOccurrence()
    {
        const string source =
            """
            public sealed class MixedHandler(IUnitOfWork unitOfWork)
            {
                public async Task Handle(CancellationToken cancellationToken)
                {
                    await unitOfWork.ExecuteResilientAsync(
                        ExecuteTransactionAsync,
                        cancellationToken);

                    async Task<bool> ExecuteTransactionAsync(
                        CancellationToken transactionCancellationToken)
                    {
                        await unitOfWork.BeginTransactionAsync(
                            transactionCancellationToken);
                        return true;
                    }

                    await unitOfWork.BeginTransactionAsync(cancellationToken);
                }
            }
            """;

        var violation = Assert.Single(
            FindManualTransactionViolations(source, "MixedHandler.cs"));

        Assert.Equal("MixedHandler.cs:17", violation);
    }

    [Fact]
    public void ManualTransactionGuard_ShouldResolveUnitOfWorkReceiverByType()
    {
        const string resilientSource =
            """
            public sealed class RenamedHandler(IUnitOfWork uow)
            {
                public Task<bool> Handle(CancellationToken cancellationToken)
                {
                    return uow.ExecuteResilientAsync(
                        async transactionCancellationToken =>
                        {
                            await uow.BeginTransactionAsync(
                                transactionCancellationToken);
                            return true;
                        },
                        cancellationToken);
                }
            }
            """;
        const string unguardedSource =
            """
            public sealed class RenamedHandler(IUnitOfWork uow)
            {
                public Task Handle(CancellationToken cancellationToken)
                {
                    return uow.BeginTransactionAsync(cancellationToken);
                }
            }
            """;

        Assert.Empty(FindManualTransactionViolations(
            resilientSource,
            "ResilientRenamedHandler.cs"));
        var violation = Assert.Single(FindManualTransactionViolations(
            unguardedSource,
            "UnguardedRenamedHandler.cs"));
        Assert.Equal("UnguardedRenamedHandler.cs:5", violation);
    }

    [Fact]
    public void ManualTransactionGuard_ShouldRejectLocalCallbackAlsoInvokedDirectly()
    {
        const string source =
            """
            public sealed class ReusedCallbackHandler(IUnitOfWork unitOfWork)
            {
                public async Task Handle(CancellationToken cancellationToken)
                {
                    await unitOfWork.ExecuteResilientAsync(
                        ExecuteTransactionAsync,
                        cancellationToken);
                    await ExecuteTransactionAsync(cancellationToken);

                    async Task<bool> ExecuteTransactionAsync(
                        CancellationToken transactionCancellationToken)
                    {
                        await unitOfWork.BeginTransactionAsync(
                            transactionCancellationToken);
                        return true;
                    }
                }
            }
            """;

        var violation = Assert.Single(
            FindManualTransactionViolations(
                source,
                "ReusedCallbackHandler.cs"));

        Assert.Equal("ReusedCallbackHandler.cs:13", violation);
    }

    [Fact]
    public void ManualTransactionGuard_ShouldRejectNestedAnonymousCallbackThatEscapes()
    {
        const string source =
            """
            public sealed class EscapedCallbackHandler(IUnitOfWork unitOfWork)
            {
                public async Task Handle(CancellationToken cancellationToken)
                {
                    Func<CancellationToken, Task>? escapedCallback = null;
                    await unitOfWork.ExecuteResilientAsync(
                        async transactionCancellationToken =>
                        {
                            escapedCallback = async nestedCancellationToken =>
                            {
                                await unitOfWork.BeginTransactionAsync(
                                    nestedCancellationToken);
                            };
                            return true;
                        },
                        cancellationToken);
                    await escapedCallback!(cancellationToken);
                }
            }
            """;

        var violation = Assert.Single(
            FindManualTransactionViolations(
                source,
                "EscapedCallbackHandler.cs"));

        Assert.Equal("EscapedCallbackHandler.cs:11", violation);
    }

    [Fact]
    public void ManualTransactionGuard_ShouldRejectAliasedTransactionMethodGroups()
    {
        const string source =
            """
            public sealed class AliasedTransactionHandler(IUnitOfWork unitOfWork)
            {
                public async Task Handle(CancellationToken cancellationToken)
                {
                    Func<CancellationToken, Task> beginTransaction =
                        unitOfWork.BeginTransactionAsync;
                    await beginTransaction(cancellationToken);
                }
            }
            """;

        var violation = Assert.Single(
            FindManualTransactionViolations(
                source,
                "AliasedTransactionHandler.cs"));

        Assert.Equal("AliasedTransactionHandler.cs:6", violation);
    }

    [Fact]
    public void ManualTransactionGuard_ShouldRejectConditionalAccessTransactions()
    {
        const string source =
            """
            public sealed class ConditionalTransactionHandler(IUnitOfWork unitOfWork)
            {
                public async Task Handle(CancellationToken cancellationToken)
                {
                    await unitOfWork?.BeginTransactionAsync(cancellationToken)!;
                }
            }
            """;

        var violation = Assert.Single(
            FindManualTransactionViolations(
                source,
                "ConditionalTransactionHandler.cs"));

        Assert.Equal("ConditionalTransactionHandler.cs:5", violation);
    }

    private static IEnumerable<string> FindManualTransactionViolations(
        string source,
        string relativePath)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var sourceTree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            relativePath);
        var globalUsingsTree = CSharpSyntaxTree.ParseText(
            """
            global using System;
            global using System.Threading;
            global using System.Threading.Tasks;
            global using IIoT.Services.Contracts.Persistence;
            """,
            parseOptions);
        var compilation = CSharpCompilation.Create(
            "ManualTransactionGuard",
            [globalUsingsTree, sourceTree],
            GetSemanticReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(sourceTree);
        var unitOfWorkType = compilation.GetTypeByMetadataName(
            typeof(IUnitOfWork).FullName!);
        Assert.NotNull(unitOfWorkType);
        var root = sourceTree.GetRoot();
        foreach (var transactionMethodGroup in root
                     .DescendantNodes()
                     .OfType<SimpleNameSyntax>()
                     .Where(methodReference => IsUnitOfWorkMethodSymbol(
                         semanticModel.GetSymbolInfo(methodReference).Symbol,
                         "BeginTransactionAsync",
                         unitOfWorkType))
                     .Where(methodReference =>
                         GetDirectInvocation(methodReference) is null))
        {
            var line = transactionMethodGroup
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition
                .Line + 1;
            yield return $"{relativePath}:{line}";
        }

        foreach (var transactionInvocation in root
                     .DescendantNodes()
                     .OfType<InvocationExpressionSyntax>()
                     .Where(invocation => IsUnitOfWorkInvocation(
                         invocation,
                         "BeginTransactionAsync",
                         semanticModel,
                         unitOfWorkType)))
        {
            if (IsInsideResilientCallback(
                    transactionInvocation,
                    semanticModel,
                    unitOfWorkType))
            {
                continue;
            }

            var line = transactionInvocation
                .GetLocation()
                .GetLineSpan()
                .StartLinePosition
                .Line + 1;
            yield return $"{relativePath}:{line}";
        }
    }

    [Fact]
    public void OidcIssuanceAndRevocation_ShouldShareTransactionLocks()
    {
        var middlewareSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "hosts", "IIoT.HttpApi", "Infrastructure", "Oidc",
                "CloudOidcIssuanceLockMiddleware.cs"));
        var programSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var issuanceLockSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "infrastructure", "IIoT.EntityFrameworkCore",
                "Identity", "HumanSessionIssuanceLock.cs"));
        var processGateSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "infrastructure", "IIoT.EntityFrameworkCore",
                "Identity", "HumanSessionIssuanceProcessGate.cs"));
        var dependencyInjectionSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "infrastructure", "IIoT.EntityFrameworkCore",
                "DependencyInjection.cs"));
        var oidcControllerSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "hosts", "IIoT.HttpApi", "Controllers", "Oidc",
                "CloudOidcController.cs"));
        var issuanceAuditSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "infrastructure", "IIoT.EntityFrameworkCore",
                "Auditing", "EfOidcIssuanceAuditTrailService.cs"));

        Assert.Contains(
            "TryExecuteAuthorizationAsync",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryExecuteTokenExchangeAsync",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExecuteBufferedAsync",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "BufferedResponseBodyFeature",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "IHttpResponseBodyFeature",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "context.Response.HasStarted",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "CloudOidcIssuanceLockMiddleware",
            programSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "RefreshTokenSubjectTransactionLock.AcquireAsync",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "AcquireOidcTokenExchangeAsync",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "CreateExecutionStrategy",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReferenceEquals(storeContext, dbContext)",
            issuanceLockSource,
            StringComparison.Ordinal);
        var executionCallbackIndex = issuanceLockSource.IndexOf(
            "async callbackToken =>",
            StringComparison.Ordinal);
        var beginTransactionIndex = issuanceLockSource.IndexOf(
            "BeginTransactionAsync",
            executionCallbackIndex,
            StringComparison.Ordinal);
        var protectedOperationIndex = issuanceLockSource.IndexOf(
            "await operation();",
            beginTransactionIndex,
            StringComparison.Ordinal);
        var commitIndex = issuanceLockSource.IndexOf(
            "CommitAsync",
            protectedOperationIndex,
            StringComparison.Ordinal);
        Assert.True(executionCallbackIndex >= 0);
        Assert.True(beginTransactionIndex > executionCallbackIndex);
        Assert.True(protectedOperationIndex > beginTransactionIndex);
        Assert.True(commitIndex > protectedOperationIndex);
        var processGateIndex = issuanceLockSource.IndexOf(
            "var processLease = await enterProcessGate",
            StringComparison.Ordinal);
        var strategyIndex = issuanceLockSource.IndexOf(
            "var strategy = dbContext.Database.CreateExecutionStrategy()",
            StringComparison.Ordinal);
        Assert.True(processGateIndex >= 0);
        Assert.True(
            strategyIndex > processGateIndex);
        Assert.Contains(
            "SemaphoreSlim(1, 1)",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal const int TokenExchangeQueueLimit = 8;",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new SemaphoreSlim(\n            TokenExchangeQueueLimit + 1,\n            TokenExchangeQueueLimit + 1)",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal const int AuthorizationRequestLimit = 16;",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal const int AuthorizationPerSubjectRequestLimit = 2;",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new SemaphoreSlim(\n            AuthorizationRequestLimit,\n            AuthorizationRequestLimit)",
            processGateSource,
            StringComparison.Ordinal);
        var authorizationPerSubjectAdmissionIndex =
            processGateSource.IndexOf(
                "entry.TryAcquireAdmission()",
                StringComparison.Ordinal);
        var authorizationGlobalAdmissionIndex = processGateSource.IndexOf(
            "_authorizationAdmissionSlots.Wait(0)",
            StringComparison.Ordinal);
        var authorizationKeyedGateIndex = processGateSource.IndexOf(
            "await entry.EnterAsync",
            StringComparison.Ordinal);
        Assert.True(authorizationPerSubjectAdmissionIndex >= 0);
        Assert.True(
            authorizationGlobalAdmissionIndex >
            authorizationPerSubjectAdmissionIndex);
        Assert.True(
            authorizationKeyedGateIndex >
            authorizationGlobalAdmissionIndex);
        Assert.Contains(
            "internal const int AuthorizationDatabaseLeaseLimit = 8;",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new SemaphoreSlim(\n            AuthorizationDatabaseLeaseLimit,\n            AuthorizationDatabaseLeaseLimit)",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Status429TooManyRequests",
            middlewareSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConcurrentDictionary<Guid, AuthorizationGateEntry>",
            processGateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddSingleton<HumanSessionIssuanceProcessGate>",
            dependencyInjectionSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "IOidcIssuanceAuditTrailService",
            dependencyInjectionSource,
            StringComparison.Ordinal);
        Assert.Equal(
            3,
            oidcControllerSource.Split(
                "WriteIssuanceSuccessAuditAsync",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "StageSuccessAsync",
            oidcControllerSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Database.CurrentTransaction",
            issuanceAuditSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "dbContext.SaveChangesAsync",
            issuanceAuditSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsStagedSuccessCommittedAsync",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new CancellationTokenSource(TimeSpan.FromSeconds(5))",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "throw new CloudWriteCommitUnknownException()",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "useExplicitIsolation: false",
            issuanceLockSource,
            StringComparison.Ordinal);
        Assert.Contains(
            ".BeginTransactionAsync(",
            issuanceLockSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UploadObservationRetention_ShouldRunIndependentlyFromDuplicateRequests()
    {
        var programSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var serviceSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "hosts", "IIoT.HttpApi", "Infrastructure",
                "UploadReceiveObservationRetentionService.cs"));
        var registrySource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "infrastructure", "IIoT.EntityFrameworkCore",
                "Uploads", "EfUploadReceiveRegistry.cs"));

        Assert.Contains(
            "UploadReceiveObservationRetentionService.Register",
            programSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "RunCleanupCycleAsync",
            serviceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Task.Delay",
            serviceSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PruneExpired",
            registrySource,
            StringComparison.Ordinal);
    }

    private static bool IsInsideResilientCallback(
        InvocationExpressionSyntax transactionInvocation,
        SemanticModel semanticModel,
        INamedTypeSymbol unitOfWorkType)
    {
        var containingCallable = transactionInvocation
            .Ancestors()
            .FirstOrDefault(node =>
                node is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MethodDeclarationSyntax);
        if (containingCallable is AnonymousFunctionExpressionSyntax callback)
        {
            return callback.Parent is ArgumentSyntax
                {
                    Parent.Parent: InvocationExpressionSyntax invocation
                }
                   && IsUnitOfWorkInvocation(
                       invocation,
                       "ExecuteResilientAsync",
                       semanticModel,
                       unitOfWorkType);
        }

        var localFunction =
            containingCallable as LocalFunctionStatementSyntax;
        var containingMethod = localFunction?
            .Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (localFunction is null || containingMethod is null)
        {
            return false;
        }

        var localFunctionSymbol =
            semanticModel.GetDeclaredSymbol(localFunction);
        if (localFunctionSymbol is null)
        {
            return false;
        }

        var callbackReferences = containingMethod
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => !localFunction.Span.Contains(identifier.Span))
            .Where(identifier => SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(identifier).Symbol,
                localFunctionSymbol))
            .ToArray();
        return callbackReferences.Length > 0
               && callbackReferences.All(identifier =>
                   identifier.Parent is ArgumentSyntax
                   {
                       Parent.Parent:
                       InvocationExpressionSyntax invocation
                   }
                   && IsUnitOfWorkInvocation(
                       invocation,
                       "ExecuteResilientAsync",
                       semanticModel,
                       unitOfWorkType));
    }

    private static bool IsUnitOfWorkInvocation(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel semanticModel,
        INamedTypeSymbol unitOfWorkType)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return IsUnitOfWorkMethodSymbol(
                   symbolInfo.Symbol,
                   methodName,
                   unitOfWorkType)
               || symbolInfo.CandidateSymbols.Any(symbol =>
                   IsUnitOfWorkMethodSymbol(
                       symbol,
                       methodName,
                       unitOfWorkType));
    }

    private static bool IsUnitOfWorkMethodSymbol(
        ISymbol? symbol,
        string methodName,
        INamedTypeSymbol unitOfWorkType)
    {
        return symbol is IMethodSymbol method
               && string.Equals(
                   method.Name,
                   methodName,
                   StringComparison.Ordinal)
               && IsUnitOfWorkType(
                   (method.ReducedFrom ?? method).ContainingType,
                   unitOfWorkType);
    }

    private static InvocationExpressionSyntax? GetDirectInvocation(
        SimpleNameSyntax methodReference)
    {
        return methodReference.Parent switch
        {
            MemberAccessExpressionSyntax
            {
                Name: var name,
                Parent: InvocationExpressionSyntax invocation
            } when name == methodReference
                   && invocation.Expression == methodReference.Parent
                => invocation,
            MemberBindingExpressionSyntax
            {
                Name: var name,
                Parent: InvocationExpressionSyntax invocation
            } when name == methodReference
                   && invocation.Expression == methodReference.Parent
                => invocation,
            _ => null
        };
    }

    private static bool IsUnitOfWorkType(
        ITypeSymbol? receiverType,
        INamedTypeSymbol unitOfWorkType)
    {
        return receiverType is not null
               && (SymbolEqualityComparer.Default.Equals(
                       receiverType,
                       unitOfWorkType)
                   || receiverType.AllInterfaces.Any(candidate =>
                       SymbolEqualityComparer.Default.Equals(
                           candidate,
                           unitOfWorkType)));
    }

    private static IReadOnlyList<MetadataReference> GetSemanticReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var referencePaths = (trustedPlatformAssemblies?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                ?? [])
            .Append(typeof(IUnitOfWork).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return referencePaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    [Fact]
    public void RefreshTokenSession_ShouldRemainInfrastructurePersistenceModel()
    {
        Assert.False(typeof(BaseEntity<Guid>).IsAssignableFrom(typeof(RefreshTokenSession)));
        Assert.False(typeof(IAggregateRoot).IsAssignableFrom(typeof(RefreshTokenSession)));
    }

    [Fact]
    public void BaseEntity_ShouldNotMakeEveryEntityAnAggregateRoot()
    {
        Assert.False(typeof(IAggregateRoot).IsAssignableFrom(typeof(BaseEntity<Guid>)));
        Assert.False(typeof(IAggregateRoot<Guid>).IsAssignableFrom(typeof(BaseEntity<Guid>)));

        Type[] aggregateRoots =
        [
            typeof(IdentityAccount),
            typeof(Employee),
            typeof(MfgProcess),
            typeof(Device),
            typeof(Recipe),
            typeof(ClientReleaseComponent),
            typeof(ClientReleaseRetentionPolicy)
        ];

        foreach (var aggregateRoot in aggregateRoots)
        {
            Assert.True(typeof(IAggregateRoot).IsAssignableFrom(aggregateRoot));
        }

        Type[] childEntities =
        [
            typeof(EmployeeDeviceAccess),
            typeof(ClientReleaseVersion),
            typeof(ClientReleaseArtifact),
            typeof(DeviceClientPluginVersion),
            typeof(DeviceClientVersionSnapshot),
            typeof(DeviceClientState),
            typeof(EdgeDeviceRuntimeHeartbeat),
            typeof(EdgeHostPlcRuntimeState)
        ];

        foreach (var childEntity in childEntities)
        {
            Assert.False(typeof(IAggregateRoot).IsAssignableFrom(childEntity));
        }
    }

    [Fact]
    public void DbContext_ShouldNotExposeReleaseChildEntitiesAsRootSets()
    {
        Assert.Null(typeof(IIoTDbContext).GetProperty("ClientReleaseVersions"));
        Assert.Null(typeof(IIoTDbContext).GetProperty("ClientReleaseArtifacts"));
        Assert.NotNull(typeof(IIoTDbContext).GetProperty(nameof(IIoTDbContext.ClientReleaseComponents)));
    }

    [Fact]
    public void DbContext_ShouldExposeOnlyEdgeHostRuntimeStateProjection()
    {
        Assert.Null(typeof(IIoTDbContext).GetProperty("EdgeHosts"));
        Assert.NotNull(typeof(IIoTDbContext).GetProperty(nameof(IIoTDbContext.EdgeHostPlcRuntimeStates)));
        Assert.Null(typeof(IIoTDbContext).GetProperty("EdgeHostPlcBindings"));
    }

    [Fact]
    public void IIoTDbContext_ShouldNotContainLegacyFlushDomainEventsPlaceholder()
    {
        Assert.Null(typeof(IIoTDbContext).GetMethod(
            "FlushDomainEventsAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic));
    }
}
