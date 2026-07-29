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
        Assert.Equal(
            2,
            refreshTokenSource.Split(
                "DeviceDeletionTransactionLock.AcquireAsync",
                StringSplitOptions.None).Length - 1);
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

    private static bool IsInsideResilientCallback(
        InvocationExpressionSyntax transactionInvocation,
        SemanticModel semanticModel,
        INamedTypeSymbol unitOfWorkType)
    {
        var hasAnonymousResilientCallback = transactionInvocation
            .Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Any(callback =>
                callback.Parent is ArgumentSyntax
                {
                    Parent.Parent: InvocationExpressionSyntax invocation
                }
                && IsUnitOfWorkInvocation(
                    invocation,
                    "ExecuteResilientAsync",
                    semanticModel,
                    unitOfWorkType));
        if (hasAnonymousResilientCallback)
        {
            return true;
        }

        var localFunction = transactionInvocation
            .Ancestors()
            .OfType<LocalFunctionStatementSyntax>()
            .FirstOrDefault();
        var containingMethod = localFunction?
            .Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (localFunction is null || containingMethod is null)
        {
            return false;
        }

        var callbackName = localFunction.Identifier.ValueText;
        return containingMethod
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => !localFunction.Span.Contains(invocation.Span))
            .Where(invocation => IsUnitOfWorkInvocation(
                invocation,
                "ExecuteResilientAsync",
                semanticModel,
                unitOfWorkType))
            .Any(invocation => invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is IdentifierNameSyntax identifier
                && string.Equals(
                    identifier.Identifier.ValueText,
                    callbackName,
                    StringComparison.Ordinal)));
    }

    private static bool IsUnitOfWorkInvocation(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel semanticModel,
        INamedTypeSymbol unitOfWorkType)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: var receiver,
            Name: SimpleNameSyntax method
        }
        && string.Equals(
            method.Identifier.ValueText,
            methodName,
            StringComparison.Ordinal)
        && IsUnitOfWorkType(
            semanticModel.GetTypeInfo(receiver).Type,
            unitOfWorkType);
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
