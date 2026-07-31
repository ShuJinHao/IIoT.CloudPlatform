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
    public void SharedAndMigrationWriteEntrypoints_ShouldHaveExplicitRecoveryClassification()
    {
        var entries = new[]
        {
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Uploads/EfUploadReceiveRegistry.cs",
                "exact-recovery",
                ["CreateExecutionStrategy()", "ObserveCommitOutcomeAsync", "callbackToken"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Uploads/EfUploadReceiveObservationRetentionPruner.cs",
                "stable-idempotent",
                ["CreateExecutionStrategy()", "callbackToken", "CleanupBatchSize"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Auditing/EfAuditTrailService.cs",
                "exact-recovery",
                ["CreateExecutionStrategy()", "ObserveCommitOutcomeAsync", "recordId"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Identity/EdgeReleaseApiKeyService.cs",
                "exact-recovery",
                ["CreateExecutionStrategy()", "ObserveCreateOutcomeAsync", "ObserveRevokeOutcomeAsync"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Identity/EfRefreshTokenService.cs",
                "exact-recovery",
                ["CreateExecutionStrategy()", "ObserveRotationOutcomeAsync", "ReplacementSession", "RefreshTokenSubjectTransactionLock"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Identity/IndependentHumanSessionRevocationService.cs",
                "exact-recovery",
                ["CreateExecutionStrategy()", "ObserveOutcomeAsync", "callbackToken", "AcquireForOidcRevocationAsync"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Outbox/OutboxMessageDispatcher.cs",
                "stable-idempotent",
                ["callbackToken", "message.Id", "PublishAsync"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.Dapper/Initializers/RecordSchemaInitializer.cs",
                "stable-idempotent",
                ["BeginTransactionAsync", "transaction: transaction", "cancellationToken"]),
            new WriteBoundaryEntry(
                "src/hosts/IIoT.MigrationWorkApp/DatabaseInitializationOrchestrator.cs",
                "exact-recovery",
                ["ExecuteFreshStageAsync", "CreateAsyncScope", "callbackToken"]),
            new WriteBoundaryEntry(
                "src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs",
                "exact-recovery",
                ["SeedRetryTarget", "CheckPasswordAsync", "SingleAdminSeedAdvisoryLockKey"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Identity/OpenIddictClientSeeder.cs",
                "stable-idempotent",
                ["FindByClientIdAsync", "CreateAsync", "UpdateAsync"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Identity/HumanSessionRevocationService.cs",
                "transaction-participant",
                ["SaveChangesAsync", "AcquireForOidcRevocationAsync"]),
            new WriteBoundaryEntry(
                "src/infrastructure/IIoT.EntityFrameworkCore/Repository/EfRepository.cs",
                "transaction-participant",
                ["SaveChangesAsync"])
        };

        Assert.Equal(13, entries.Length);
        Assert.Equal(7, entries.Count(entry => entry.Classification == "exact-recovery"));
        Assert.Equal(4, entries.Count(entry => entry.Classification == "stable-idempotent"));
        Assert.Equal(2, entries.Count(entry => entry.Classification == "transaction-participant"));

        foreach (var entry in entries)
        {
            var path = CloudRepositoryPath.Find(
                entry.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
            var source = File.ReadAllText(path);
            foreach (var marker in entry.RequiredMarkers)
            {
                Assert.Contains(marker, source, StringComparison.Ordinal);
            }

            if (entry.Classification == "transaction-participant")
            {
                Assert.DoesNotContain("CreateExecutionStrategy()", source, StringComparison.Ordinal);
            }
        }

        var employeeMutationFiles = new[]
        {
            "UpdateEmployeeRole.cs",
            "DeactivateEmployee.cs",
            "TerminateEmployee.cs",
            "ActivateEmployee.cs"
        };
        foreach (var fileName in employeeMutationFiles)
        {
            var source = File.ReadAllText(CloudRepositoryPath.Find(
                "src", "services", "IIoT.EmployeeService", "Commands", "Human",
                "Employees", fileName));
            Assert.Contains("ExecuteResilientAsync", source, StringComparison.Ordinal);
            Assert.Contains("sessionRevocationService.RevokeAllAsync", source, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(CloudRepositoryPath.Find(
            "src", "services", "IIoT.Services.Contracts", "Contracts", "Messaging",
            "IIntegrationEventOutbox.cs")));
        Assert.False(File.Exists(CloudRepositoryPath.Find(
            "src", "infrastructure", "IIoT.EntityFrameworkCore", "Outbox",
            "EfIntegrationEventOutbox.cs")));
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

    private sealed record WriteBoundaryEntry(
        string RelativePath,
        string Classification,
        IReadOnlyList<string> RequiredMarkers);

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
