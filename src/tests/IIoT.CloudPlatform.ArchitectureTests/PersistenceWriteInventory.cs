using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IIoT.CloudPlatform.ArchitectureTests;

internal static class PersistenceWriteInventory
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview);

    private static readonly SymbolDisplayFormat MethodDisplayFormat =
        new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions:
                SymbolDisplayMemberOptions.IncludeContainingType |
                SymbolDisplayMemberOptions.IncludeParameters |
                SymbolDisplayMemberOptions.IncludeExplicitInterface,
            parameterOptions:
                SymbolDisplayParameterOptions.IncludeType |
                SymbolDisplayParameterOptions.IncludeName,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly ImmutableHashSet<string> FailClosedCandidateNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "SaveChanges",
            "SaveChangesAsync",
            "BeginTransaction",
            "BeginTransactionAsync",
            "Commit",
            "CommitAsync",
            "Rollback",
            "RollbackAsync",
            "ExecuteSqlRaw",
            "ExecuteSqlRawAsync",
            "ExecuteSqlInterpolated",
            "ExecuteSqlInterpolatedAsync",
            "ExecuteUpdate",
            "ExecuteUpdateAsync",
            "ExecuteDelete",
            "ExecuteDeleteAsync",
            "ExecuteNonQuery",
            "ExecuteNonQueryAsync",
            "Migrate",
            "MigrateAsync",
            "EnsureCreated",
            "EnsureCreatedAsync",
            "EnsureDeleted",
            "EnsureDeletedAsync");

    private static readonly ImmutableHashSet<string> DapperCandidateNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Execute",
            "ExecuteAsync");

    private static readonly ImmutableHashSet<string> IdentityCandidateNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "CreateAsync",
            "UpdateAsync",
            "DeleteAsync",
            "AddToRoleAsync",
            "RemoveFromRoleAsync",
            "ResetPasswordAsync",
            "RemoveFromRolesAsync",
            "AddClaimAsync",
            "AddClaimsAsync",
            "RemoveClaimAsync",
            "RemoveClaimsAsync",
            "AddPasswordAsync",
            "RemovePasswordAsync",
            "ChangePasswordAsync",
            "SetPasswordHashAsync",
            "AccessFailedAsync",
            "ResetAccessFailedCountAsync",
            "SetLockoutEnabledAsync");

    private static readonly PersistenceEvidence ArchitectureEvidence = new(
        "src/tests/IIoT.CloudPlatform.ArchitectureTests/PersistenceBoundaryArchitectureTests.cs",
        "ProductionPersistenceWriteEntrypoints_ShouldBeDynamicallyClassified");

    public static PersistenceInventoryResult DiscoverProduction()
    {
        var repositoryRoot = Directory.GetParent(CloudRepositoryPath.Find("src"))!.FullName;
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var references = CreateMetadataReferences();
        var entries = new List<PersistenceWriteEntry>();
        var unresolved = new List<string>();

        foreach (var projectPath in Directory
                     .GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
                     .Where(IsProductionProject)
                     .Order(StringComparer.Ordinal))
        {
            DiscoverProject(
                repositoryRoot,
                projectPath,
                references,
                entries,
                unresolved);
        }

        var orderedEntries = entries
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Method, StringComparer.Ordinal)
            .ToArray();
        return new PersistenceInventoryResult(
            orderedEntries,
            orderedEntries
                .Where(entry => entry.Classification is null)
                .ToArray(),
            unresolved
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public static PersistenceInventoryResult DiscoverSnippet(
        string source,
        string relativePath = "InventoryFixture.cs")
    {
        var absolutePath = Path.Combine(
            Environment.CurrentDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, absolutePath);
        var references = CreateMetadataReferences().Values;
        var compilation = CSharpCompilation.Create(
            "PersistenceInventoryFixture",
            [CreateImplicitUsingsTree(absolutePath), tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var sites = new List<WriteSite>();
        var unresolved = new List<string>();
        DiscoverTreeWriteSites(
            Environment.CurrentDirectory,
            tree,
            model,
            sites,
            unresolved);
        var models = new Dictionary<SyntaxTree, SemanticModel>
        {
            [tree] = model
        };
        var graph = ProtectionGraph.Create([tree], models);
        var entries = CreateEntries(sites, graph);
        return new PersistenceInventoryResult(
            entries,
            entries.Where(entry => entry.Classification is null).ToArray(),
            unresolved
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public static bool IsIncludedProductionSource(string path)
        => !IsExcludedSource(path) && IsProductionSourcePath(path);

    public static void AssertEvidenceExists(PersistenceEvidence evidence)
    {
        var path = CloudRepositoryPath.Find(
            evidence.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions).GetRoot();
        var testMethod = root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method =>
                string.Equals(method.Identifier.ValueText, evidence.TestMethod, StringComparison.Ordinal));

        Assert.NotNull(testMethod);
        Assert.Contains(
            testMethod.AttributeLists.SelectMany(list => list.Attributes),
            attribute => attribute.Name.ToString() is "Fact" or "Theory" or "FactAttribute" or "TheoryAttribute");
        Assert.True(
            testMethod.Body is { Statements.Count: > 0 } || testMethod.ExpressionBody is not null,
            $"Persistence evidence test has no body: {evidence.RelativePath}::{evidence.TestMethod}");
    }

    private static void DiscoverProject(
        string repositoryRoot,
        string projectPath,
        IReadOnlyDictionary<string, MetadataReference> allReferences,
        ICollection<PersistenceWriteEntry> entries,
        ICollection<string> unresolved)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourcePaths = Directory
            .GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedSource(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (sourcePaths.Length == 0)
        {
            return;
        }

        var trees = sourcePaths
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                ParseOptions,
                path))
            .ToArray();
        var assemblyName = GetAssemblyName(projectPath);
        var references = allReferences
            .Where(pair => !string.Equals(
                pair.Key,
                assemblyName + ".dll",
                StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value);
        var compilation = CSharpCompilation.Create(
            assemblyName + ".PersistenceInventory",
            trees.Prepend(CreateImplicitUsingsTree(projectPath)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));

        var models = trees.ToDictionary(
            tree => tree,
            tree => compilation.GetSemanticModel(tree, ignoreAccessibility: true));
        var projectWriteSites = new List<WriteSite>();
        foreach (var tree in trees)
        {
            DiscoverTreeWriteSites(
                repositoryRoot,
                tree,
                models[tree],
                projectWriteSites,
                unresolved);
        }

        var protectionGraph = ProtectionGraph.Create(trees, models);
        foreach (var entry in CreateEntries(projectWriteSites, protectionGraph))
        {
            entries.Add(entry);
        }
    }

    private static IReadOnlyList<PersistenceWriteEntry> CreateEntries(
        IReadOnlyCollection<WriteSite> writeSites,
        ProtectionGraph protectionGraph)
    {
        return writeSites
            .GroupBy(site => site.Callable, SymbolEqualityComparer.Default)
            .Select(group =>
            {
                var sites = group.ToArray();
                var first = sites[0];
                var automaticClassification = sites.All(site =>
                    protectionGraph.IsInsideReplayRoot(site.Syntax, site.Callable));
                var classification = automaticClassification
                    ? PersistenceWriteClassification.ExecutionStrategyReplayRoot
                    : ClassifyKnownBoundary(first.Callable);
                var evidence = ResolveEvidence(
                    first.RelativePath,
                    first.Callable,
                    classification);
                return new PersistenceWriteEntry(
                    first.RelativePath,
                    first.Line,
                    first.Callable.ToDisplayString(MethodDisplayFormat),
                    sites.Select(site => site.Kind)
                        .Distinct()
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    classification,
                    evidence);
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Method, StringComparer.Ordinal)
            .ToArray();
    }

    private static void DiscoverTreeWriteSites(
        string repositoryRoot,
        SyntaxTree tree,
        SemanticModel semanticModel,
        ICollection<WriteSite> sites,
        ICollection<string> unresolved)
    {
        var root = tree.GetRoot();
        var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, tree.FilePath));
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var methods = GetMethodSymbols(symbolInfo);
            var sink = methods
                .Select(method =>
                    TryClassifySink(method, out var kind)
                        ? (Method: method, Kind: kind)
                        : ((IMethodSymbol Method, string Kind)?)null)
                .FirstOrDefault(candidate => candidate is not null);
            if (sink is null)
            {
                if (methods.Count == 0 &&
                    IsUnresolvedPersistenceInvocation(invocation, semanticModel))
                {
                    var name = TryGetInvocationName(invocation) ?? "<unknown>";
                    unresolved.Add(FormatLocation(relativePath, invocation, name));
                }

                continue;
            }

            var (method, kind) = sink.Value;

            var callable = semanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            if (callable is null)
            {
                unresolved.Add(FormatLocation(relativePath, invocation, method.Name));
                continue;
            }

            sites.Add(new WriteSite(
                relativePath,
                GetLine(invocation),
                invocation,
                callable,
                kind));
        }

        foreach (var methodReference in root
                     .DescendantNodes()
                     .OfType<SimpleNameSyntax>()
                     .Where(reference => GetDirectInvocation(reference) is null))
        {
            var methodSymbols = GetMethodSymbols(
                semanticModel.GetSymbolInfo(methodReference));
            var sink = methodSymbols
                .Select(method =>
                    TryClassifySink(method, out var kind)
                        ? (Method: method, Kind: kind)
                        : ((IMethodSymbol Method, string Kind)?)null)
                .FirstOrDefault(candidate => candidate is not null);
            if (sink is null)
            {
                if (methodSymbols.Count == 0 &&
                    IsUnresolvedPersistenceReference(
                        methodReference,
                        semanticModel))
                {
                    unresolved.Add(FormatLocation(
                        relativePath,
                        methodReference,
                        methodReference.Identifier.ValueText));
                }

                continue;
            }

            var (symbol, kind) = sink.Value;

            var callable = semanticModel.GetEnclosingSymbol(methodReference.SpanStart) as IMethodSymbol;
            if (callable is null)
            {
                unresolved.Add(FormatLocation(relativePath, methodReference, symbol.Name));
                continue;
            }

            sites.Add(new WriteSite(
                relativePath,
                GetLine(methodReference),
                methodReference,
                callable,
                kind + "-method-group"));
        }
    }

    private static PersistenceWriteClassification? ClassifyKnownBoundary(
        IMethodSymbol callable)
    {
        var typeName = callable.ContainingType?.ToDisplayString() ?? string.Empty;
        var methodName = callable.Name;

        if (typeName is
            "IIoT.EntityFrameworkCore.IIoTDbContext" or
            "IIoT.EntityFrameworkCore.Repository.EfRepository<T>" or
            "IIoT.EntityFrameworkCore.ClientReleases.EfClientReleaseComponentDeletionStore" or
            "IIoT.EntityFrameworkCore.ClientReleases.EfDeviceClientStateStore" or
            "IIoT.EntityFrameworkCore.EdgeHosts.EfEdgeHostPlcRuntimeStateStore" or
            "IIoT.EntityFrameworkCore.Identity.HumanSessionRevocationService" or
            "IIoT.EntityFrameworkCore.Persistence.EfUnitOfWork" or
            "IIoT.EntityFrameworkCore.Persistence.DeviceDeletionTransactionLock" or
            "IIoT.EntityFrameworkCore.Persistence.RefreshTokenSubjectTransactionLock")
        {
            return PersistenceWriteClassification.TransactionParticipant;
        }

        if ((typeName ==
                 "IIoT.EntityFrameworkCore.Auditing.EfOidcIssuanceAuditTrailService" &&
             methodName == "StageSuccessAsync") ||
            (typeName ==
                 "IIoT.EntityFrameworkCore.Identity.EmployeeMutationVersionStore" &&
             methodName == "TryAdvanceAsync") ||
            (typeName == "IIoT.EntityFrameworkCore.Identity.IdentityAccountStore" &&
             methodName is "CreateAsync" or "CompareExchangeStateAsync" or
                 "DeleteAsync" or "AssignRoleAsync" or "ReplaceAssignableRoleAsync") ||
            (typeName == "IIoT.EntityFrameworkCore.Identity.IdentityPasswordService" &&
             methodName == "SetPasswordAsync"))
        {
            return PersistenceWriteClassification.TransactionParticipant;
        }

        if ((typeName == "IIoT.Dapper.Initializers.RecordSchemaInitializer" &&
             methodName == "InitializeAsync") ||
            (typeName ==
                 "IIoT.Dapper.Production.Repositories.Capacities.HourlyCapacityRecordRepository" &&
             methodName == "UpsertAsync") ||
            (typeName ==
                 "IIoT.Dapper.Production.Repositories.DeviceLogs.DeviceLogRecordRepository" &&
             methodName == "InsertBatchAsync") ||
            (typeName ==
                 "IIoT.Dapper.Production.Repositories.PassStations.PassStationRecordRepository" &&
             methodName == "InsertBatchAsync") ||
            (typeName == "IIoT.EntityFrameworkCore.Identity.OpenIddictClientSeeder" &&
             methodName == "EnsureAicopilotClientAsync"))
        {
            return PersistenceWriteClassification.StableKeyOrExactObservation;
        }

        if ((typeName == "IIoT.EntityFrameworkCore.Uploads.EfUploadReceiveRegistry" &&
             methodName == "RecordDuplicateObservationAsync") ||
            (typeName ==
                 "IIoT.ProductionService.Commands.ClientReleases.PublishEdgePluginPackageHandler" &&
             methodName == "Handle") ||
            (typeName ==
                 "IIoT.ProductionService.Commands.ClientReleases.PublishEdgeReleaseBundleHandler" &&
             methodName == "Handle"))
        {
            return PersistenceWriteClassification.StableKeyOrExactObservation;
        }

        return null;
    }

    private static PersistenceEvidence ResolveEvidence(
        string relativePath,
        IMethodSymbol callable,
        PersistenceWriteClassification? classification)
    {
        const string retryTests =
            "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ProductionRetryTransactionPostgresTests.cs";
        var normalized = NormalizePath(relativePath);
        var typeName = callable.ContainingType?.ToDisplayString() ?? string.Empty;

        if (normalized.Contains("/services/IIoT.EmployeeService/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete");
        }

        if (normalized.Contains("/services/IIoT.MasterDataService/", StringComparison.Ordinal) ||
            normalized.Contains("/services/IIoT.ProductionService/Commands/Human/Devices/", StringComparison.Ordinal) ||
            normalized.Contains("/services/IIoT.ProductionService/Commands/Human/Recipes/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss");
        }

        if (normalized.Contains("/services/IIoT.ProductionService/Commands/Edge/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "EdgeReports_ShouldRecoverCommitConfirmationLoss");
        }

        if (normalized.Contains("/services/IIoT.ProductionService/", StringComparison.Ordinal) &&
            normalized.Contains("ClientRelease", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
                "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss");
        }

        if (normalized.Contains("/infrastructure/IIoT.Dapper/Initializers/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/RecordSchemaInitializerPostgresTests.cs",
                "RecordSchemas_FirstAndWarmRun_ShouldConvergeToRequiredTables");
        }

        if (normalized.Contains("/infrastructure/IIoT.Dapper/Production/Repositories/Capacities/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/CapacityPersistencePostgresTests.cs",
                "UpsertAsync_LateSmallerSnapshotCannotReplaceCompletedClipCount");
        }

        if (normalized.Contains("/infrastructure/IIoT.Dapper/Production/Repositories/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/CapacityPersistencePostgresTests.cs",
                "PassStationAndDeviceLogWrites_ShouldRemainIdempotent");
        }

        if (normalized.Contains("/hosts/IIoT.MigrationWorkApp/SeedData/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/SingleAdminInvariantPostgresTests.cs",
                "PasswordRepairCommitConfirmationLoss_ShouldConfirmTargetWithoutSecondAdmin");
        }

        if (normalized.Contains("/hosts/IIoT.MigrationWorkApp/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/DatabaseSchemaCompatibilityPostgresTests.cs",
                "LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres");
        }

        if (typeName is
            "IIoT.EntityFrameworkCore.Identity.RolePolicyService" or
            "IIoT.EntityFrameworkCore.Identity.IdentityPasswordService")
        {
            return new PersistenceEvidence(
                retryTests,
                "IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly");
        }

        if (normalized.Contains("/Auditing/EfOidcIssuanceAuditTrailService.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Identity/HumanSessionIssuanceLock.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "OidcIssuanceSuccessAudit_ShouldCommitAtomicallyWithGrant");
        }

        if (normalized.Contains("/Auditing/EfAuditTrailService.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseComponentDeletionPostgresTests.cs",
                "EfAuditTrailService_ShouldPersistOneExactRecordPerIdempotencyKey");
        }

        if (normalized.Contains("/Identity/EdgeReleaseApiKeyService.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext");
        }

        if (normalized.Contains("/Identity/EfRefreshTokenService.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "HumanRefreshRotation_ShouldRecoverCommitLossAndRejectSourceReplay");
        }

        if (normalized.Contains("/Identity/IndependentHumanSessionRevocationService.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Identity/HumanSessionRevocationService.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "IndependentHumanSessionRevocation_ShouldRecoverCommitLossExactly");
        }

        if (normalized.Contains("/Identity/OpenIddictClientSeeder.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/OpenIddictClientSeederPostgresTests.cs",
                "OidcClientSeed_FirstWarmAndCommitLoss_ShouldConvergeToOneClientId");
        }

        if (normalized.Contains("/Uploads/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "UploadRegistrationAndOutbox_ShouldRecoverCommitLossAsOneLogicalMessage");
        }

        if (normalized.Contains("/Outbox/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/OutboxDispatchPersistenceTests.cs",
                "OutboxCommitTransient_ShouldRepublishStableIdentityAndReceiverInboxApplyBusinessEffectOnce");
        }

        if (normalized.Contains("/QueryServices/EfDeviceDeletionDependencyService.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete");
        }

        if (normalized.Contains("/Identity/EmployeeMutationObservationReader.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Persistence/CloudWriteObservationReader.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "EmployeeMutationObservation_ShouldUseOneSnapshotAcrossConcurrentMutation");
        }

        if (normalized.Contains("/ClientReleases/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                "src/tests/IIoT.CloudPlatform.Persistence.PostgresTests/ClientReleaseWriteRetryPostgresTests.cs",
                "ClientReleaseWrites_ShouldReplayTransientAndRecoverCommitConfirmationLoss");
        }

        if (normalized.Contains("/EdgeHosts/", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "EdgeReports_ShouldRecoverCommitConfirmationLoss");
        }

        if (normalized.Contains("/Identity/IdentityAccountStore.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Identity/EmployeeMutationVersionStore.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Repository/EfRepository.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Persistence/EfUnitOfWork.cs", StringComparison.Ordinal) ||
            normalized.EndsWith("/IIoTDbContext.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete");
        }

        if (normalized.Contains("/Persistence/DeviceDeletionTransactionLock.cs", StringComparison.Ordinal) ||
            normalized.Contains("/Persistence/RefreshTokenSubjectTransactionLock.cs", StringComparison.Ordinal))
        {
            return new PersistenceEvidence(
                retryTests,
                "IndependentHumanSessionRevocation_ShouldSeeSessionCommittedWhileWaitingForSubjectLock");
        }

        _ = classification;
        return ArchitectureEvidence;
    }

    private static bool TryClassifySink(IMethodSymbol symbol, out string kind)
    {
        var method = symbol.ReducedFrom ?? symbol;
        var type = method.ContainingType;
        var typeName = type?.ToDisplayString() ?? string.Empty;
        var namespaceName = type?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if ((method.Name is "SaveChanges" or "SaveChangesAsync") &&
            (InheritsFrom(type, "Microsoft.EntityFrameworkCore.DbContext") ||
             IsIiotRepositoryOrStore(type)))
        {
            kind = "ef-save";
            return true;
        }

        if ((method.Name is "BeginTransaction" or "BeginTransactionAsync" or
             "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync") &&
            IsTransactionType(type, namespaceName))
        {
            kind = "manual-transaction";
            return true;
        }

        if (method.Name.StartsWith("ExecuteSql", StringComparison.Ordinal) &&
            namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            kind = "ef-raw-sql";
            return true;
        }

        if ((method.Name is "ExecuteUpdate" or "ExecuteUpdateAsync" or
             "ExecuteDelete" or "ExecuteDeleteAsync") &&
            namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            kind = "ef-bulk-write";
            return true;
        }

        if ((method.Name is "Execute" or "ExecuteAsync") &&
            typeName == "Dapper.SqlMapper")
        {
            kind = "dapper-write";
            return true;
        }

        if ((method.Name is "ExecuteNonQuery" or "ExecuteNonQueryAsync") &&
            (InheritsFrom(type, "System.Data.Common.DbCommand") ||
             typeName is "System.Data.IDbCommand" or "System.Data.Common.DbCommand"))
        {
            kind = "db-command-write";
            return true;
        }

        if ((method.Name is "Migrate" or "MigrateAsync" or
             "EnsureCreated" or "EnsureCreatedAsync" or
             "EnsureDeleted" or "EnsureDeletedAsync") &&
            namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            kind = "migration";
            return true;
        }

        if (namespaceName.StartsWith("OpenIddict", StringComparison.Ordinal) &&
            (method.Name is "CreateAsync" or "UpdateAsync" or "DeleteAsync"))
        {
            kind = "oidc-seed";
            return true;
        }

        if (namespaceName.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) &&
            (method.Name is "CreateAsync" or "UpdateAsync" or "DeleteAsync" or
             "AddToRoleAsync" or "RemoveFromRoleAsync" or "ResetPasswordAsync" or
             "RemoveFromRolesAsync" or "AddClaimAsync" or "AddClaimsAsync" or
             "RemoveClaimAsync" or "RemoveClaimsAsync" or "AddPasswordAsync" or
             "RemovePasswordAsync" or "ChangePasswordAsync" or
             "SetPasswordHashAsync" or "AccessFailedAsync" or
             "ResetAccessFailedCountAsync" or "SetLockoutEnabledAsync"))
        {
            kind = "identity-seed";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private static bool IsIiotRepositoryOrStore(INamedTypeSymbol? type)
    {
        if (type is null ||
            !type.ContainingNamespace.ToDisplayString().StartsWith("IIoT", StringComparison.Ordinal))
        {
            return false;
        }

        return type.Name.Contains("Repository", StringComparison.Ordinal) ||
               type.Name.Contains("Store", StringComparison.Ordinal) ||
               type.AllInterfaces.Any(candidate =>
                   candidate.Name.Contains("Repository", StringComparison.Ordinal) ||
                   candidate.Name.Contains("Store", StringComparison.Ordinal));
    }

    private static bool IsTransactionType(INamedTypeSymbol? type, string namespaceName)
    {
        if (type is null)
        {
            return false;
        }

        return namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
               namespaceName.StartsWith("System.Data", StringComparison.Ordinal) ||
               namespaceName.StartsWith("Npgsql", StringComparison.Ordinal) ||
               type.AllInterfaces.Any(candidate =>
                   candidate.ToDisplayString() is
                       "IIoT.Services.Contracts.Persistence.IUnitOfWork" or
                       "Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction" or
                       "System.Data.IDbTransaction");
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, MetadataReference> CreateMetadataReferences()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        foreach (var path in trustedPlatformAssemblies?
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, path) in paths)
        {
            try
            {
                references.Add(fileName, MetadataReference.CreateFromFile(path));
            }
            catch (BadImageFormatException)
            {
                // Native testhost dependencies are not compiler references.
            }
        }

        return references;
    }

    private static SyntaxTree CreateImplicitUsingsTree(string projectPath)
    {
        return CSharpSyntaxTree.ParseText(
            """
            global using System;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Linq;
            global using System.Net.Http;
            global using System.Threading;
            global using System.Threading.Tasks;
            """,
            ParseOptions,
            projectPath + ".PersistenceInventory.GlobalUsings.g.cs");
    }

    private static string GetAssemblyName(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
                   .Descendants()
                   .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                   ?.Value.Trim()
               ?? Path.GetFileNameWithoutExtension(projectPath);
    }

    private static bool IsProductionProject(string projectPath)
    {
        return IsProductionSourcePath(projectPath);
    }

    private static bool IsProductionSourcePath(string path)
    {
        var normalized = "/" + NormalizePath(path).TrimStart('/');
        return !normalized.Contains("/src/tests/", StringComparison.Ordinal) &&
               !normalized.Contains("/src/testing/", StringComparison.Ordinal) &&
               !normalized.Contains("/src/analyzers/", StringComparison.Ordinal);
    }

    private static bool IsExcludedSource(string path)
    {
        var normalized = NormalizePath(path);
        var fileName = Path.GetFileName(path);
        return normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal) ||
               normalized.Contains("/Migrations/", StringComparison.Ordinal) ||
               fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static InvocationExpressionSyntax? GetDirectInvocation(SimpleNameSyntax methodReference)
    {
        return methodReference.Parent switch
        {
            MemberAccessExpressionSyntax
            {
                Name: var name,
                Parent: InvocationExpressionSyntax invocation
            } when name == methodReference && invocation.Expression == methodReference.Parent => invocation,
            MemberBindingExpressionSyntax
            {
                Name: var name,
                Parent: InvocationExpressionSyntax invocation
            } when name == methodReference && invocation.Expression == methodReference.Parent => invocation,
            _ => null
        };
    }

    private static IReadOnlyList<IMethodSymbol> GetMethodSymbols(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return [method];
        }

        return symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .ToArray();
    }

    private static string? TryGetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            SimpleNameSyntax name => name.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: var name } => name.Identifier.ValueText,
            MemberBindingExpressionSyntax { Name: var name } => name.Identifier.ValueText,
            _ => null
        };
    }

    private static bool IsUnresolvedPersistenceInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (TryGetInvocationName(invocation) is not { } name)
        {
            return false;
        }

        if (FailClosedCandidateNames.Contains(name))
        {
            return true;
        }

        var reference = invocation.Expression switch
        {
            SimpleNameSyntax simpleName => simpleName,
            MemberAccessExpressionSyntax { Name: var simpleName } => simpleName,
            MemberBindingExpressionSyntax { Name: var simpleName } => simpleName,
            _ => null
        };
        return reference is not null &&
               IsUnresolvedPersistenceReference(reference, semanticModel);
    }

    private static bool IsUnresolvedPersistenceReference(
        SimpleNameSyntax reference,
        SemanticModel semanticModel)
    {
        var name = reference.Identifier.ValueText;
        if (FailClosedCandidateNames.Contains(name))
        {
            return true;
        }

        var receiverType = GetReceiverType(reference, semanticModel);
        if (receiverType is null)
        {
            return false;
        }

        var namespaceName = receiverType.ContainingNamespace?.ToDisplayString()
            ?? string.Empty;
        return (DapperCandidateNames.Contains(name) &&
                IsDatabaseConnectionType(receiverType)) ||
               (IdentityCandidateNames.Contains(name) &&
                (namespaceName.StartsWith(
                     "Microsoft.AspNetCore.Identity",
                     StringComparison.Ordinal) ||
                 namespaceName.StartsWith("OpenIddict", StringComparison.Ordinal)));
    }

    private static ITypeSymbol? GetReceiverType(
        SimpleNameSyntax reference,
        SemanticModel semanticModel)
    {
        ExpressionSyntax? receiver = reference.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name == reference => memberAccess.Expression,
            MemberBindingExpressionSyntax memberBinding
                when memberBinding.Name == reference => memberBinding
                    .Ancestors()
                    .OfType<ConditionalAccessExpressionSyntax>()
                    .FirstOrDefault()
                    ?.Expression,
            _ => null
        };
        return receiver is null
            ? null
            : semanticModel.GetTypeInfo(receiver).Type;
    }

    private static bool IsDatabaseConnectionType(ITypeSymbol type)
    {
        var namedType = type as INamedTypeSymbol;
        return (type.ToDisplayString() is
                    "System.Data.IDbConnection" or
                    "System.Data.Common.DbConnection") ||
               namedType?.AllInterfaces.Any(candidate =>
                   candidate.ToDisplayString() == "System.Data.IDbConnection") == true ||
               InheritsFrom(namedType, "System.Data.Common.DbConnection");
    }

    private static string FormatLocation(string relativePath, SyntaxNode syntax, string name)
        => $"{relativePath}:{GetLine(syntax)} unresolved {name}";

    private static int GetLine(SyntaxNode syntax)
        => syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed class ProtectionGraph
    {
        private readonly IReadOnlyDictionary<IMethodSymbol, IReadOnlyList<SyntaxNode>> _references;
        private readonly IReadOnlyDictionary<SyntaxTree, SemanticModel> _models;
        private readonly Dictionary<IMethodSymbol, bool> _memo =
            new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _visiting =
            new(SymbolEqualityComparer.Default);

        private ProtectionGraph(
            IReadOnlyDictionary<IMethodSymbol, IReadOnlyList<SyntaxNode>> references,
            IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
        {
            _references = references;
            _models = models;
        }

        public static ProtectionGraph Create(
            IReadOnlyCollection<SyntaxTree> trees,
            IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
        {
            var references = new Dictionary<IMethodSymbol, List<SyntaxNode>>(
                SymbolEqualityComparer.Default);
            foreach (var tree in trees)
            {
                var model = models[tree];
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    ISymbol? symbol = node switch
                    {
                        InvocationExpressionSyntax invocation =>
                            model.GetSymbolInfo(invocation).Symbol,
                        SimpleNameSyntax name when GetDirectInvocation(name) is null =>
                            model.GetSymbolInfo(name).Symbol,
                        _ => null
                    };
                    if (symbol is not IMethodSymbol method ||
                        !method.Locations.Any(location => location.IsInSource))
                    {
                        continue;
                    }

                    if (!references.TryGetValue(method, out var methodReferences))
                    {
                        methodReferences = [];
                        references.Add(method, methodReferences);
                    }

                    methodReferences.Add(node);
                }
            }

            var readOnlyReferences =
                new Dictionary<IMethodSymbol, IReadOnlyList<SyntaxNode>>(
                    SymbolEqualityComparer.Default);
            foreach (var (method, methodReferences) in references)
            {
                readOnlyReferences.Add(method, methodReferences);
            }

            return new ProtectionGraph(readOnlyReferences, models);
        }

        public bool IsInsideReplayRoot(SyntaxNode writeSyntax, IMethodSymbol callable)
        {
            var model = _models[writeSyntax.SyntaxTree];
            if (IsInsideProtectedDelegate(writeSyntax, model))
            {
                return true;
            }

            return IsProtectedCallable(callable);
        }

        private bool IsProtectedCallable(IMethodSymbol callable)
        {
            if (_memo.TryGetValue(callable, out var cached))
            {
                return cached;
            }

            if (!_visiting.Add(callable))
            {
                return false;
            }

            try
            {
                if (!_references.TryGetValue(callable, out var references) || references.Count == 0)
                {
                    return _memo[callable] = false;
                }

                var protectedEverywhere = references.All(reference =>
                {
                    var model = _models[reference.SyntaxTree];
                    if (IsProtectedDelegateArgument(reference, model) ||
                        IsInsideProtectedDelegate(reference, model))
                    {
                        return true;
                    }

                    var caller = model.GetEnclosingSymbol(reference.SpanStart) as IMethodSymbol;
                    return caller is not null &&
                           !SymbolEqualityComparer.Default.Equals(caller, callable) &&
                           IsProtectedCallable(caller);
                });
                return _memo[callable] = protectedEverywhere;
            }
            finally
            {
                _visiting.Remove(callable);
            }
        }

        private bool IsInsideProtectedDelegate(SyntaxNode node, SemanticModel model)
        {
            foreach (var anonymous in node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>())
            {
                if (IsProtectedDelegateArgument(anonymous, model))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsProtectedDelegateArgument(SyntaxNode node, SemanticModel model)
        {
            var argument = node.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            if (argument?.Parent?.Parent is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (model.GetOperation(invocation) is not IInvocationOperation invocationOperation ||
                model.GetOperation(argument) is not IArgumentOperation argumentOperation ||
                argumentOperation.Parameter is not { } parameter)
            {
                return false;
            }

            return IsProtectedExecutor(invocationOperation.TargetMethod) &&
                   IsReplayOperationParameter(parameter);
        }

        private static bool IsReplayOperationParameter(IParameterSymbol parameter)
            => parameter.Type.TypeKind == TypeKind.Delegate &&
               parameter.Name is "operation" or "attempt" or "stage";

        private bool IsProtectedExecutor(IMethodSymbol symbol)
        {
            var method = symbol.ReducedFrom ?? symbol;
            if (method.Name == "ExecuteResilientAsync" &&
                (method.ContainingType.ToDisplayString() ==
                 "IIoT.Services.Contracts.Persistence.IUnitOfWork" ||
                 method.ContainingType.AllInterfaces.Any(candidate =>
                     candidate.ToDisplayString() ==
                     "IIoT.Services.Contracts.Persistence.IUnitOfWork")))
            {
                return true;
            }

            if (IsExecutionStrategyExecutor(symbol))
            {
                return true;
            }

            var isKnownHelper =
                (method.Name == "ExecuteFreshStageAsync" &&
                 method.ContainingType.ToDisplayString() ==
                 "IIoT.MigrationWorkApp.DatabaseInitializationOrchestrator") ||
                (method.Name == "ExecuteRecoverableAsync" &&
                 method.ContainingType.ToDisplayString() is
                     "IIoT.EntityFrameworkCore.Identity.RolePolicyService" or
                     "IIoT.EntityFrameworkCore.Identity.IdentityPasswordService");
            return isKnownHelper && DirectlyInvokesExecutionStrategy(method);
        }

        private bool DirectlyInvokesExecutionStrategy(IMethodSymbol method)
        {
            foreach (var syntaxReference in method.DeclaringSyntaxReferences)
            {
                var declaration = syntaxReference.GetSyntax();
                if (!_models.TryGetValue(declaration.SyntaxTree, out var model))
                {
                    continue;
                }

                if (declaration
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .SelectMany(invocation =>
                        GetMethodSymbols(model.GetSymbolInfo(invocation)))
                    .Any(IsExecutionStrategyExecutor))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExecutionStrategyExecutor(IMethodSymbol symbol)
        {
            var method = symbol.ReducedFrom ?? symbol;
            var namespaceName = method.ContainingNamespace.ToDisplayString();
            return (method.Name is "Execute" or "ExecuteAsync") &&
                   (symbol.ReceiverType?.ToDisplayString() ==
                    "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy" ||
                    method.ContainingType.ToDisplayString() is
                        "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy" or
                        "Microsoft.EntityFrameworkCore.ExecutionStrategyExtensions" ||
                    namespaceName.StartsWith(
                        "Microsoft.EntityFrameworkCore.Storage",
                        StringComparison.Ordinal));
        }
    }

    private sealed record WriteSite(
        string RelativePath,
        int Line,
        SyntaxNode Syntax,
        IMethodSymbol Callable,
        string Kind);
}

internal sealed record PersistenceInventoryResult(
    IReadOnlyList<PersistenceWriteEntry> Entries,
    IReadOnlyList<PersistenceWriteEntry> UnclassifiedEntries,
    IReadOnlyList<string> UnresolvedCandidates);

internal sealed record PersistenceWriteEntry(
    string RelativePath,
    int Line,
    string Method,
    IReadOnlyList<string> SinkKinds,
    PersistenceWriteClassification? Classification,
    PersistenceEvidence Evidence)
{
    public string Diagnostic =>
        $"{RelativePath}:{Line} {Method} [{string.Join(",", SinkKinds)}]";
}

internal sealed record PersistenceEvidence(string RelativePath, string TestMethod);

internal enum PersistenceWriteClassification
{
    ExecutionStrategyReplayRoot,
    TransactionParticipant,
    StableKeyOrExactObservation
}
