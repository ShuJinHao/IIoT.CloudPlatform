using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IIoT.CloudPlatform.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CloudArchitectureAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> RepositoryWriteMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Add",
            "AddAsync",
            "Update",
            "Delete",
            "Remove",
            "SaveChanges",
            "SaveChangesAsync");

    private static readonly ImmutableHashSet<string> DatabaseWriteMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Add",
            "AddAsync",
            "AddRange",
            "AddRangeAsync",
            "Update",
            "UpdateRange",
            "Remove",
            "RemoveRange",
            "SaveChanges",
            "SaveChangesAsync",
            "Execute",
            "ExecuteAsync",
            "ExecuteNonQuery",
            "ExecuteNonQueryAsync",
            "ExecuteSqlRaw",
            "ExecuteSqlRawAsync",
            "ExecuteSqlInterpolated",
            "ExecuteSqlInterpolatedAsync");

    private static readonly ImmutableHashSet<string> RawDatabaseAccessMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ExecuteReader",
            "ExecuteReaderAsync",
            "ExecuteScalar",
            "ExecuteScalarAsync",
            "Query",
            "QueryAsync",
            "QueryFirst",
            "QueryFirstAsync",
            "QueryFirstOrDefault",
            "QueryFirstOrDefaultAsync",
            "QuerySingle",
            "QuerySingleAsync",
            "QuerySingleOrDefault",
            "QuerySingleOrDefaultAsync",
            "QueryMultiple",
            "QueryMultipleAsync");

    private static readonly ImmutableHashSet<string> ConnectionResourceLiterals =
        ImmutableHashSet.Create(StringComparer.Ordinal, "iiot-db", "eventbus");

    private static readonly Regex ReadOnlySqlStart = new(
        @"^(SELECT|WITH)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SqlSelectKeyword = new(
        @"\bSELECT\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SqlWriteOrDdlKeyword = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|UPSERT|REPLACE|INTO|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|CALL|EXEC|EXECUTE|COPY|VACUUM|ANALYZE|LOCK|COMMENT|REINDEX|CLUSTER|REFRESH)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SqlFunctionCall = new(
        "(?<![\\w$\\\"])(?<name>(?:[A-Za-z_][A-Za-z0-9_$]*|\\\"[^\\\"]*\\\")(?:\\s*\\.\\s*(?:[A-Za-z_][A-Za-z0-9_$]*|\\\"[^\\\"]*\\\"))?)\\s*\\(",
        RegexOptions.CultureInvariant);

    private static readonly ImmutableHashSet<string> ProvenReadOnlySqlFunctions =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "abs",
            "avg",
            "ceil",
            "ceiling",
            "char_length",
            "coalesce",
            "concat",
            "count",
            "date_part",
            "date_trunc",
            "extract",
            "floor",
            "greatest",
            "json_array_length",
            "jsonb_array_length",
            "least",
            "length",
            "lower",
            "max",
            "min",
            "nullif",
            "round",
            "row_number",
            "sum",
            "trim",
            "upper");

    private static readonly ImmutableHashSet<string> SqlStructuralParentheses =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "as",
            "exists",
            "filter",
            "in",
            "over",
            "values");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            CloudArchitectureDiagnostics.LayerDependency,
            CloudArchitectureDiagnostics.AggregateBoundary,
            CloudArchitectureDiagnostics.DatabaseOwner,
            CloudArchitectureDiagnostics.AiReadWritePath,
            CloudArchitectureDiagnostics.AiReadAuthorization,
            CloudArchitectureDiagnostics.ProductionTestReference,
            CloudArchitectureDiagnostics.SecurityReadCachePath,
            CloudArchitectureDiagnostics.UnsignedJwtParsing,
            CloudArchitectureDiagnostics.RetiredServicesCommonNamespace,
            CloudArchitectureDiagnostics.ConnectionResourceLiteral);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilationAnalysis);
    }

    private static void StartCompilationAnalysis(CompilationStartAnalysisContext context)
    {
        var state = new CompilationState(context.Compilation, context.Options.AnalyzerConfigOptionsProvider);

        context.RegisterSymbolAction(state.AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSymbolAction(state.AnalyzeParameter, SymbolKind.Parameter);
        context.RegisterSymbolAction(state.AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(state.AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(state.AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(state.AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(state.AnalyzeVariableDeclarator, OperationKind.VariableDeclarator);
        context.RegisterOperationAction(state.AnalyzeSimpleAssignment, OperationKind.SimpleAssignment);
        context.RegisterOperationAction(state.AnalyzeFieldInitializer, OperationKind.FieldInitializer);
        context.RegisterOperationAction(state.AnalyzePropertyInitializer, OperationKind.PropertyInitializer);
        context.RegisterOperationAction(state.AnalyzeReturn, OperationKind.Return);
        context.RegisterOperationAction(state.AnalyzeDynamicInvocation, OperationKind.DynamicInvocation);
        context.RegisterOperationAction(state.AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(state.AnalyzeLiteral, OperationKind.Literal);
        context.RegisterCompilationEndAction(state.AnalyzeCompilationEnd);
    }

    private enum CloudLayer
    {
        Unknown,
        Shared,
        Core,
        Service,
        Infrastructure,
        Host
    }

    private sealed class CompilationState
    {
        private readonly Compilation _compilation;
        private readonly string _assemblyName;
        private readonly CloudLayer _layer;
        private readonly INamedTypeSymbol? _aggregateRoot;
        private readonly INamedTypeSymbol? _repository;
        private readonly INamedTypeSymbol? _readRepository;
        private readonly INamedTypeSymbol? _humanRequest;
        private readonly INamedTypeSymbol? _deviceRequest;
        private readonly INamedTypeSymbol? _anonymousBootstrapRequest;
        private readonly INamedTypeSymbol? _publicRequest;
        private readonly INamedTypeSymbol? _aiReadRequest;
        private readonly INamedTypeSymbol? _authorizeAiReadAttribute;
        private readonly INamedTypeSymbol? _authorizeRequirementAttribute;
        private readonly INamedTypeSymbol? _adminOnlyAttribute;
        private readonly INamedTypeSymbol? _command;
        private readonly INamedTypeSymbol? _cacheService;
        private readonly INamedTypeSymbol? _readOnlyQueryPort;
        private readonly INamedTypeSymbol? _jwtSecurityTokenHandler;
        private readonly ImmutableArray<INamedTypeSymbol> _securityReadInterfaces;
        private readonly ImmutableHashSet<string> _databaseAllowedProjects;
        private readonly ImmutableHashSet<string> _databaseAllowedTypes;
        private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<InvocationEdge>> _callGraph =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _aiReadHandlerRoots =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _readOnlyQueryPortRoots =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _securityReadRoots =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<ISymbol, ConcurrentBag<DelegateBinding>> _delegateBindings =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentBag<StoredDelegateInvocation> _storedDelegateInvocations = new();
        private readonly ConcurrentBag<DelegateArgumentFlow> _delegateArgumentFlows = new();

        internal CompilationState(
            Compilation compilation,
            AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider)
        {
            _compilation = compilation;
            _assemblyName = compilation.AssemblyName ?? string.Empty;
            _layer = ClassifyAssembly(_assemblyName);
            _aggregateRoot = compilation.GetTypeByMetadataName("IIoT.SharedKernel.Domain.IAggregateRoot");
            _repository = compilation.GetTypeByMetadataName("IIoT.SharedKernel.Repository.IRepository`1");
            _readRepository = compilation.GetTypeByMetadataName("IIoT.SharedKernel.Repository.IReadRepository`1");
            _humanRequest = compilation.GetTypeByMetadataName("IIoT.Services.Contracts.IHumanRequest`1");
            _deviceRequest = compilation.GetTypeByMetadataName("IIoT.Services.Contracts.IDeviceRequest`1");
            _anonymousBootstrapRequest = compilation.GetTypeByMetadataName(
                "IIoT.Services.Contracts.IAnonymousBootstrapRequest`1");
            _publicRequest = compilation.GetTypeByMetadataName("IIoT.Services.Contracts.IPublicRequest`1");
            _aiReadRequest = compilation.GetTypeByMetadataName("IIoT.Services.Contracts.IAiReadRequest`1");
            _authorizeAiReadAttribute = compilation.GetTypeByMetadataName(
                "IIoT.Services.CrossCutting.Attributes.AuthorizeAiReadAttribute");
            _authorizeRequirementAttribute = compilation.GetTypeByMetadataName(
                "IIoT.Services.CrossCutting.Attributes.AuthorizeRequirementAttribute");
            _adminOnlyAttribute = compilation.GetTypeByMetadataName(
                "IIoT.Services.CrossCutting.Attributes.AdminOnlyAttribute");
            _command = compilation.GetTypeByMetadataName("IIoT.SharedKernel.Messaging.ICommand`1");
            _cacheService = compilation.GetTypeByMetadataName("IIoT.Services.Contracts.ICacheService");
            _readOnlyQueryPort = compilation.GetTypeByMetadataName(
                "IIoT.SharedKernel.Architecture.IReadOnlyQueryPort");
            _jwtSecurityTokenHandler = compilation.GetTypeByMetadataName(
                "System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler");
            _securityReadInterfaces = new[]
                {
                    compilation.GetTypeByMetadataName("IIoT.Services.Contracts.Authorization.IPermissionProvider"),
                    compilation.GetTypeByMetadataName("IIoT.Services.Contracts.Authorization.IDevicePermissionService"),
                    compilation.GetTypeByMetadataName("IIoT.Services.Contracts.RecordQueries.IDeviceIdentityQueryService")
                }
                .Where(static symbol => symbol is not null)
                .Cast<INamedTypeSymbol>()
                .ToImmutableArray();
            _databaseAllowedProjects = ReadOptionSet(
                analyzerConfigOptionsProvider,
                "dotnet_diagnostic.cloudarch003.allowed_projects");
            _databaseAllowedTypes = ReadOptionSet(
                analyzerConfigOptionsProvider,
                "dotnet_diagnostic.cloudarch003.allowed_types");
        }

        internal void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (!type.Locations.Any(static location => location.IsInSource))
                return;

            AnalyzeAggregateOwner(context, type);
            AnalyzeRepositoryType(context, type, type.BaseType);
            foreach (var @interface in type.Interfaces)
                AnalyzeRepositoryType(context, type, @interface);

            AnalyzeAiReadAuthorization(context, type);
            AnalyzeRetiredServicesCommonNamespace(context, type);
            CaptureAiReadHandlerRoots(type);
            CaptureSecurityReadRoots(type);
            CaptureInterfaceDispatch(type);
            CaptureReadOnlyQueryPortRoots(type);
        }

        internal void AnalyzeParameter(SymbolAnalysisContext context)
        {
            var parameter = (IParameterSymbol)context.Symbol;
            AnalyzeRepositoryType(context, parameter, parameter.Type);
            AnalyzeDatabaseTypeUse(context, parameter, parameter.Type);
        }

        internal void AnalyzeField(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            AnalyzeRepositoryType(context, field, field.Type);
            AnalyzeDatabaseTypeUse(context, field, field.Type);
        }

        internal void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;
            AnalyzeRepositoryType(context, property, property.Type);
            AnalyzeDatabaseTypeUse(context, property, property.Type);
        }

        internal void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            AnalyzeRepositoryType(context, method, method.ReturnType);
            AnalyzeDatabaseTypeUse(context, method, method.ReturnType);
        }

        internal void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            AnalyzeRepositoryOperationResult(context, invocation);
            AnalyzeUnsignedJwtParsing(context, invocation);

            var caller = GetOperationCaller(invocation, context.ContainingSymbol);
            var target = NormalizeMethod(invocation.TargetMethod);
            if (caller is not null && target is not null)
            {
                if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke ||
                    IsSystemDelegateDynamicInvoke(invocation.TargetMethod))
                {
                    CaptureInvokedDelegate(
                        caller,
                        invocation.Instance,
                        invocation.Syntax.GetLocation());
                }
                else
                {
                    var edge = new InvocationEdge(
                        target,
                        invocation.Syntax.GetLocation(),
                        IsDirectWriteSink(invocation, caller, target),
                        target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                    _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>()).Add(edge);
                }

                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter is not null &&
                        argument.Parameter.Type.TypeKind == TypeKind.Delegate)
                    {
                        _delegateArgumentFlows.Add(new DelegateArgumentFlow(
                            caller,
                            target,
                            NormalizeParameter(argument.Parameter),
                            argument.Value,
                            invocation.Syntax.GetLocation(),
                            !HasExecutableSource(target)));
                    }
                }
            }

            if (ShouldEnforceDatabaseOwner(context.ContainingSymbol) &&
                IsDatabaseApiType(invocation.TargetMethod.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CloudArchitectureDiagnostics.DatabaseOwner,
                    invocation.Syntax.GetLocation(),
                    context.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }

        internal void AnalyzeVariableDeclarator(OperationAnalysisContext context)
        {
            var declarator = (IVariableDeclaratorOperation)context.Operation;
            if (declarator.Initializer is not null)
                CaptureStoredDelegateBinding(declarator.Symbol, declarator.Initializer.Value);
        }

        internal void AnalyzeSimpleAssignment(OperationAnalysisContext context)
        {
            var assignment = (ISimpleAssignmentOperation)context.Operation;
            var storage = TryGetDelegateStorage(assignment.Target);
            if (storage is not null)
                CaptureStoredDelegateBinding(storage, assignment.Value);
        }

        internal void AnalyzeFieldInitializer(OperationAnalysisContext context)
        {
            var initializer = (IFieldInitializerOperation)context.Operation;
            foreach (var field in initializer.InitializedFields)
                CaptureStoredDelegateBinding(field, initializer.Value);
        }

        internal void AnalyzePropertyInitializer(OperationAnalysisContext context)
        {
            var initializer = (IPropertyInitializerOperation)context.Operation;
            foreach (var property in initializer.InitializedProperties)
                CaptureStoredDelegateBinding(property, initializer.Value);
        }

        internal void AnalyzeReturn(OperationAnalysisContext context)
        {
            var returned = (IReturnOperation)context.Operation;
            if (returned.ReturnedValue is null ||
                context.ContainingSymbol is not IMethodSymbol containingMethod ||
                containingMethod.ReturnType.TypeKind != TypeKind.Delegate)
            {
                return;
            }

            var factory = NormalizeMethod(containingMethod);
            if (factory is not null)
                CaptureStoredDelegateBinding(factory, returned.ReturnedValue);
        }

        internal void AnalyzeDynamicInvocation(OperationAnalysisContext context)
        {
            var caller = GetOperationCaller(context.Operation, context.ContainingSymbol);
            if (caller is null)
                return;

            _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>()).Add(
                new InvocationEdge(
                    caller,
                    context.Operation.Syntax.GetLocation(),
                    isDirectWriteSink: true,
                    targetDisplay: "unresolved dynamic invocation",
                    isUnresolvedDynamic: true));
        }

        private void CaptureInvokedDelegate(
            IMethodSymbol caller,
            IOperation? operation,
            Location location)
        {
            if (operation is null)
            {
                AddUnresolvedDelegateEdge(caller, location);
                return;
            }

            switch (operation)
            {
                case IConversionOperation conversion:
                    CaptureInvokedDelegate(caller, conversion.Operand, location);
                    return;
                case IDelegateCreationOperation delegateCreation:
                    CaptureInvokedDelegate(caller, delegateCreation.Target, location);
                    return;
                case IAnonymousFunctionOperation anonymousFunction:
                    AddDelegateEdge(caller, anonymousFunction.Symbol, location);
                    return;
                case IMethodReferenceOperation methodReference:
                    AddDelegateEdge(caller, methodReference.Method, location);
                    return;
                case IConditionalOperation conditional:
                    CaptureInvokedDelegate(caller, conditional.WhenTrue, location);
                    CaptureInvokedDelegate(caller, conditional.WhenFalse, location);
                    return;
                case ICoalesceOperation coalesce:
                    if (!IsConstantNull(coalesce.Value))
                        CaptureInvokedDelegate(caller, coalesce.Value, location);
                    CaptureInvokedDelegate(caller, coalesce.WhenNull, location);
                    return;
            }

            var storage = TryGetDelegateStorage(operation) ?? TryGetDelegateFactory(operation);
            if (storage is not null)
            {
                _storedDelegateInvocations.Add(new StoredDelegateInvocation(caller, storage, location));
                return;
            }

            AddUnresolvedDelegateEdge(caller, location);
        }

        private void CaptureStoredDelegateBinding(ISymbol storage, IOperation operation)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    CaptureStoredDelegateBinding(storage, conversion.Operand);
                    return;
                case IDelegateCreationOperation delegateCreation:
                    CaptureStoredDelegateBinding(storage, delegateCreation.Target);
                    return;
                case IAnonymousFunctionOperation anonymousFunction:
                    AddDelegateBinding(storage, anonymousFunction.Symbol);
                    return;
                case IMethodReferenceOperation methodReference:
                    AddDelegateBinding(storage, methodReference.Method);
                    return;
                case IConditionalOperation conditional:
                    CaptureStoredDelegateBinding(storage, conditional.WhenTrue);
                    if (conditional.WhenFalse is not null)
                    {
                        CaptureStoredDelegateBinding(storage, conditional.WhenFalse);
                    }
                    else
                    {
                        _delegateBindings.GetOrAdd(storage, static _ => new ConcurrentBag<DelegateBinding>())
                            .Add(DelegateBinding.Unresolved);
                    }
                    return;
                case ICoalesceOperation coalesce:
                    if (!IsConstantNull(coalesce.Value))
                        CaptureStoredDelegateBinding(storage, coalesce.Value);
                    CaptureStoredDelegateBinding(storage, coalesce.WhenNull);
                    return;
            }

            var sourceStorage = TryGetDelegateStorage(operation) ?? TryGetDelegateFactory(operation);
            if (sourceStorage is not null && !SymbolEqualityComparer.Default.Equals(storage, sourceStorage))
            {
                _delegateBindings.GetOrAdd(storage, static _ => new ConcurrentBag<DelegateBinding>())
                    .Add(DelegateBinding.ForStorage(sourceStorage));
                return;
            }

            _delegateBindings.GetOrAdd(storage, static _ => new ConcurrentBag<DelegateBinding>())
                .Add(DelegateBinding.Unresolved);
        }

        private void AddDelegateBinding(ISymbol storage, IMethodSymbol method)
        {
            var target = NormalizeMethod(method);
            if (target is null)
                return;

            _delegateBindings.GetOrAdd(storage, static _ => new ConcurrentBag<DelegateBinding>())
                .Add(DelegateBinding.ForTarget(target));
        }

        private static ISymbol? TryGetDelegateStorage(IOperation? operation)
        {
            while (operation is IConversionOperation conversion)
                operation = conversion.Operand;

            return operation switch
            {
                ILocalReferenceOperation local => local.Local,
                IFieldReferenceOperation field => field.Field,
                IPropertyReferenceOperation property => property.Property,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null
            };
        }

        private static ISymbol? TryGetDelegateFactory(IOperation operation)
        {
            operation = UnwrapConversion(operation);
            if (operation is not IInvocationOperation invocation ||
                invocation.Type?.TypeKind != TypeKind.Delegate)
            {
                return null;
            }

            return NormalizeMethod(invocation.TargetMethod);
        }

        private static IParameterSymbol NormalizeParameter(IParameterSymbol parameter)
        {
            if (parameter.ContainingSymbol is not IMethodSymbol method)
                return parameter;

            var normalizedMethod = NormalizeMethod(method);
            return normalizedMethod is not null && parameter.Ordinal < normalizedMethod.Parameters.Length
                ? normalizedMethod.Parameters[parameter.Ordinal]
                : parameter;
        }

        private static bool HasExecutableSource(IMethodSymbol method)
        {
            foreach (var syntaxReference in method.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax();
                if (syntax is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax declaration &&
                    (declaration.Body is not null || declaration.ExpressionBody is not null))
                {
                    return true;
                }

                if (syntax is Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunction &&
                    (localFunction.Body is not null || localFunction.ExpressionBody is not null))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSystemDelegateDynamicInvoke(IMethodSymbol method)
        {
            return string.Equals(method.Name, "DynamicInvoke", StringComparison.Ordinal) &&
                   string.Equals(
                       method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                       "System.Delegate",
                       StringComparison.Ordinal);
        }

        private static bool IsConstantNull(IOperation operation)
        {
            operation = UnwrapConversion(operation);
            return operation.ConstantValue.HasValue && operation.ConstantValue.Value is null;
        }

        private void AddDelegateEdge(IMethodSymbol caller, IMethodSymbol targetMethod, Location location)
        {
            var target = NormalizeMethod(targetMethod);
            if (target is null || SymbolEqualityComparer.Default.Equals(caller, target))
                return;

            _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>()).Add(
                new InvocationEdge(
                    target,
                    location,
                    isDirectWriteSink: false,
                    target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private void AddUnresolvedDelegateEdge(IMethodSymbol caller, Location location)
        {
            _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>()).Add(
                new InvocationEdge(
                    caller,
                    location,
                    isDirectWriteSink: true,
                    targetDisplay: "unresolved invoked delegate",
                    isUnresolvedDynamic: true));
        }

        private void AnalyzeUnsignedJwtParsing(
            OperationAnalysisContext context,
            IInvocationOperation invocation)
        {
            if (IsTestOnlyAssembly(_assemblyName) || _jwtSecurityTokenHandler is null)
                return;

            var method = invocation.TargetMethod;
            if (!string.Equals(method.Name, "ReadJwtToken", StringComparison.Ordinal) ||
                (!SymbolEqualityComparer.Default.Equals(method.ContainingType, _jwtSecurityTokenHandler) &&
                 !SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, _jwtSecurityTokenHandler)))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.UnsignedJwtParsing,
                invocation.Syntax.GetLocation(),
                context.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private void AnalyzeRetiredServicesCommonNamespace(
            SymbolAnalysisContext context,
            INamedTypeSymbol type)
        {
            if (IsTestOnlyAssembly(_assemblyName))
                return;

            var namespaceName = type.ContainingNamespace.ToDisplayString();
            if (!string.Equals(namespaceName, "IIoT.Services.Common", StringComparison.Ordinal) &&
                !namespaceName.StartsWith("IIoT.Services.Common.", StringComparison.Ordinal))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.RetiredServicesCommonNamespace,
                GetSourceLocation(type),
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                namespaceName));
        }

        internal void AnalyzeLiteral(OperationAnalysisContext context)
        {
            if (IsTestOnlyAssembly(_assemblyName) ||
                context.Operation is not ILiteralOperation literal ||
                !literal.ConstantValue.HasValue ||
                literal.ConstantValue.Value is not string value ||
                !ConnectionResourceLiterals.Contains(value))
            {
                return;
            }

            var containingTypeName = context.ContainingSymbol.ContainingType?
                .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (string.Equals(
                    containingTypeName,
                    "IIoT.SharedKernel.Configuration.ConnectionResourceNames",
                    StringComparison.Ordinal))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.ConnectionResourceLiteral,
                literal.Syntax.GetLocation(),
                context.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                value));
        }

        internal void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            AnalyzeRepositoryOperationResult(context, creation);

            if (!ShouldEnforceDatabaseOwner(context.ContainingSymbol))
                return;

            if (!IsDatabaseApiType(creation.Type))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.DatabaseOwner,
                creation.Syntax.GetLocation(),
                context.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                creation.Constructor?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    ?? creation.Type?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    ?? "unknown database type"));
        }

        internal void AnalyzeCompilationEnd(CompilationAnalysisContext context)
        {
            MaterializeStoredDelegateInvocations();
            AnalyzeLayerDependencies(context);
            AnalyzeProductionTestReferences(context);
            AnalyzeAiReadWritePaths(context);
            AnalyzeSecurityReadCachePaths(context);
        }

        private void MaterializeStoredDelegateInvocations()
        {
            var consumedParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            foreach (var invocation in _storedDelegateInvocations)
            {
                var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                var contextParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
                var fullyResolved = ResolveStoredDelegateTargets(
                    invocation.Storage,
                    new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                    targets,
                    contextParameters);
                foreach (var target in targets)
                {
                    AddDelegateEdge(invocation.Caller, target, invocation.Location);
                }

                foreach (var parameter in contextParameters)
                    consumedParameters.Add(parameter);

                if (!fullyResolved || contextParameters.Any(parameter => !HasInvocationBinding(parameter)))
                {
                    AddUnresolvedDelegateEdge(invocation.Caller, invocation.Location);
                }
            }

            foreach (var flow in _delegateArgumentFlows)
            {
                if (flow.ExternalConsumer)
                    consumedParameters.Add(flow.Parameter);
            }

            bool changed;
            do
            {
                changed = false;
                foreach (var flow in _delegateArgumentFlows)
                {
                    if (!consumedParameters.Contains(flow.Parameter))
                        continue;

                    var contextParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
                    ResolveDelegateOperation(
                        flow.Actual,
                        new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                        contextParameters);
                    foreach (var parameter in contextParameters)
                        changed |= consumedParameters.Add(parameter);
                }
            } while (changed);

            foreach (var flow in _delegateArgumentFlows)
            {
                if (!consumedParameters.Contains(flow.Parameter))
                    continue;

                var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                var contextParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
                var fullyResolved = ResolveDelegateOperation(
                    flow.Actual,
                    new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                    targets,
                    contextParameters);
                foreach (var target in targets)
                    AddDelegateEdge(flow.Caller, target, flow.Location);

                if (!fullyResolved || contextParameters.Any(parameter => !HasInvocationBinding(parameter)))
                    AddUnresolvedDelegateEdge(flow.Caller, flow.Location);
            }

            foreach (var parameter in consumedParameters)
            {
                var owner = NormalizeMethod(parameter.ContainingSymbol as IMethodSymbol);
                if (owner is not null &&
                    (_aiReadHandlerRoots.ContainsKey(owner) || _securityReadRoots.ContainsKey(owner)))
                {
                    AddUnresolvedDelegateEdge(owner, GetSourceLocation(parameter));
                }
            }
        }

        private bool HasInvocationBinding(IParameterSymbol parameter)
        {
            return _delegateArgumentFlows.Any(flow =>
                SymbolEqualityComparer.Default.Equals(flow.Parameter, parameter));
        }

        private bool ResolveDelegateOperation(
            IOperation operation,
            HashSet<ISymbol> visited,
            HashSet<IMethodSymbol> targets,
            HashSet<IParameterSymbol> contextParameters)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    return ResolveDelegateOperation(
                        conversion.Operand,
                        visited,
                        targets,
                        contextParameters);
                case IDelegateCreationOperation delegateCreation:
                    return ResolveDelegateOperation(
                        delegateCreation.Target,
                        visited,
                        targets,
                        contextParameters);
                case IAnonymousFunctionOperation anonymousFunction:
                    var anonymousTarget = NormalizeMethod(anonymousFunction.Symbol);
                    if (anonymousTarget is null)
                        return false;
                    targets.Add(anonymousTarget);
                    return true;
                case IMethodReferenceOperation methodReference:
                    var methodTarget = NormalizeMethod(methodReference.Method);
                    if (methodTarget is null)
                        return false;
                    targets.Add(methodTarget);
                    return true;
                case IConditionalOperation conditional:
                    return ResolveDelegateOperation(
                               conditional.WhenTrue,
                               visited,
                               targets,
                               contextParameters) &
                           (conditional.WhenFalse is not null && ResolveDelegateOperation(
                               conditional.WhenFalse,
                               visited,
                               targets,
                               contextParameters));
                case ICoalesceOperation coalesce:
                    var valueResolved = IsConstantNull(coalesce.Value) || ResolveDelegateOperation(
                        coalesce.Value,
                        visited,
                        targets,
                        contextParameters);
                    return valueResolved & ResolveDelegateOperation(
                        coalesce.WhenNull,
                        visited,
                        targets,
                        contextParameters);
            }

            var storage = TryGetDelegateStorage(operation) ?? TryGetDelegateFactory(operation);
            return storage is not null && ResolveStoredDelegateTargets(
                storage,
                visited,
                targets,
                contextParameters);
        }

        private bool ResolveStoredDelegateTargets(
            ISymbol storage,
            HashSet<ISymbol> visited,
            HashSet<IMethodSymbol> targets,
            HashSet<IParameterSymbol> contextParameters)
        {
            if (!visited.Add(storage))
                return false;

            if (!_delegateBindings.TryGetValue(storage, out var bindings))
            {
                visited.Remove(storage);
                if (storage is IParameterSymbol parameter)
                {
                    contextParameters.Add(NormalizeParameter(parameter));
                    return true;
                }

                return false;
            }

            try
            {
                var hasBinding = false;
                var fullyResolved = true;
                foreach (var binding in bindings)
                {
                    hasBinding = true;
                    if (binding.Target is not null)
                    {
                        targets.Add(binding.Target);
                    }
                    else if (binding.Storage is not null)
                    {
                        fullyResolved &= ResolveStoredDelegateTargets(
                            binding.Storage,
                            visited,
                            targets,
                            contextParameters);
                    }
                    else
                    {
                        fullyResolved = false;
                    }
                }

                return hasBinding && fullyResolved;
            }
            finally
            {
                visited.Remove(storage);
            }
        }

        private void AnalyzeAggregateOwner(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate || _aggregateRoot is null)
                return;

            if (!Implements(type, _aggregateRoot))
                return;

            if (_layer == CloudLayer.Core)
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.AggregateBoundary,
                GetSourceLocation(type),
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                $"聚合根只能由 IIoT.Core.* 声明，当前程序集为 '{_assemblyName}'"));
        }

        private void AnalyzeRepositoryType(SymbolAnalysisContext context, ISymbol owner, ITypeSymbol? type)
        {
            if (owner is INamedTypeSymbol ownerType && IsRepositoryDefinition(ownerType.OriginalDefinition))
                return;

            if (type is null || !TryFindInvalidRepositoryEntity(type, out var entity))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.AggregateBoundary,
                GetSourceLocation(owner),
                owner.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                $"仓储实体 '{entity.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' 未实现 IAggregateRoot"));
        }

        private bool TryFindInvalidRepositoryEntity(ITypeSymbol type, out ITypeSymbol entity)
        {
            entity = type;
            if (type is IArrayTypeSymbol array)
                return TryFindInvalidRepositoryEntity(array.ElementType, out entity);

            if (type is not INamedTypeSymbol namedType)
                return false;

            if (IsRepositoryDefinition(namedType.OriginalDefinition))
            {
                entity = namedType.TypeArguments[0];
                return !ImplementsAggregateRoot(entity);
            }

            foreach (var @interface in namedType.AllInterfaces)
            {
                if (!IsRepositoryDefinition(@interface.OriginalDefinition))
                    continue;

                entity = @interface.TypeArguments[0];
                if (!ImplementsAggregateRoot(entity))
                    return true;
            }

            foreach (var argument in namedType.TypeArguments)
            {
                if (TryFindInvalidRepositoryEntity(argument, out entity))
                    return true;
            }

            return false;
        }

        private void AnalyzeRepositoryOperationResult(
            OperationAnalysisContext context,
            IOperation operation)
        {
            if (operation.Type is null ||
                !TryFindInvalidRepositoryEntity(operation.Type, out var entity))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.AggregateBoundary,
                operation.Syntax.GetLocation(),
                context.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                $"操作结果中的仓储实体 '{entity.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' 未实现 IAggregateRoot"));
        }

        private bool IsRepositoryDefinition(INamedTypeSymbol type)
        {
            if (_repository is not null && SymbolEqualityComparer.Default.Equals(type, _repository))
                return true;
            if (_readRepository is not null && SymbolEqualityComparer.Default.Equals(type, _readRepository))
                return true;

            return type.Arity == 1 &&
                   (type.Name == "IRepository" || type.Name == "IReadRepository") &&
                   type.ContainingNamespace.ToDisplayString() == "IIoT.SharedKernel.Repository";
        }

        private bool ImplementsAggregateRoot(ITypeSymbol type)
        {
            if (_aggregateRoot is null)
                return true;

            if (type is ITypeParameterSymbol parameter)
                return parameter.ConstraintTypes.Any(ImplementsAggregateRoot);

            return Implements(type, _aggregateRoot);
        }

        private void AnalyzeAiReadAuthorization(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate)
                return;

            var isAiRead = IsAiReadRequest(type);
            var isHuman = ImplementsOpenGeneric(type, _humanRequest);
            var requestKindCount = CountRequestKinds(type);
            var aiAttributes = _authorizeAiReadAttribute is null
                ? Array.Empty<AttributeData>()
                : GetAttributesIncludingBase(type, _authorizeAiReadAttribute).ToArray();
            var humanAttributes = _authorizeRequirementAttribute is null
                ? Array.Empty<AttributeData>()
                : GetAttributesIncludingBase(type, _authorizeRequirementAttribute).ToArray();
            var adminAttributes = _adminOnlyAttribute is null
                ? Array.Empty<AttributeData>()
                : GetAttributesIncludingBase(type, _adminOnlyAttribute).ToArray();

            if (requestKindCount > 1)
            {
                ReportAuthorization(context, type, "请求不得同时实现多个 HTTP request-kind marker");
                return;
            }

            if (isAiRead && aiAttributes.Length == 0)
            {
                ReportAuthorization(context, type, "IAiReadRequest 缺少 AuthorizeAiReadAttribute");
                return;
            }

            if (!isAiRead && aiAttributes.Length > 0)
            {
                ReportAuthorization(context, type, "AuthorizeAiReadAttribute 只能用于 IAiReadRequest");
                return;
            }

            if ((humanAttributes.Length > 0 || adminAttributes.Length > 0) && !isHuman)
            {
                ReportAuthorization(
                    context,
                    type,
                    "AuthorizeRequirementAttribute 和 AdminOnlyAttribute 只能用于 IHumanRequest");
                return;
            }

            if (!isAiRead)
                return;

            foreach (var attribute in aiAttributes)
            {
                var permission = attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
                const string permissionPrefix = "AiRead.";
                if (permission is null ||
                    !permission.StartsWith(permissionPrefix, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(permission.Substring(permissionPrefix.Length)))
                {
                    ReportAuthorization(context, type, "AuthorizeAiRead 权限必须是带非空后缀的 AiRead.* 常量");
                    return;
                }
            }
        }

        private static IEnumerable<AttributeData> GetAttributesIncludingBase(
            INamedTypeSymbol type,
            INamedTypeSymbol attributeType)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var attribute in current.GetAttributes())
                {
                    if (attribute.AttributeClass is not null &&
                        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                    {
                        yield return attribute;
                    }
                }
            }
        }

        private static void ReportAuthorization(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.AiReadAuthorization,
                GetSourceLocation(type),
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                reason));
        }

        private void CaptureAiReadHandlerRoots(INamedTypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate)
                return;

            foreach (var @interface in type.AllInterfaces)
            {
                if (@interface.TypeArguments.Length != 2 || !IsQueryHandlerInterface(@interface))
                    continue;
                if (!IsAiReadRequest(@interface.TypeArguments[0]))
                    continue;

                foreach (var member in @interface.GetMembers("Handle").OfType<IMethodSymbol>())
                {
                    if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation)
                    {
                        var normalized = NormalizeMethod(implementation);
                        if (normalized is not null)
                            _aiReadHandlerRoots.TryAdd(normalized, 0);
                    }
                }
            }
        }

        private void CaptureInterfaceDispatch(INamedTypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate)
                return;

            foreach (var @interface in type.AllInterfaces)
            {
                foreach (var interfaceMethod in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation ||
                        !implementation.Locations.Any(static location => location.IsInSource))
                    {
                        continue;
                    }

                    var dispatchTarget = NormalizeMethod(interfaceMethod);
                    var implementationTarget = NormalizeMethod(implementation);
                    if (dispatchTarget is null || implementationTarget is null ||
                        SymbolEqualityComparer.Default.Equals(dispatchTarget, implementationTarget))
                    {
                        continue;
                    }

                    _callGraph.GetOrAdd(dispatchTarget, static _ => new ConcurrentBag<InvocationEdge>()).Add(
                        new InvocationEdge(
                            implementationTarget,
                            GetSourceLocation(implementation),
                            isDirectWriteSink: false,
                            implementationTarget.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                }
            }
        }

        private void CaptureReadOnlyQueryPortRoots(INamedTypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate || _readOnlyQueryPort is null)
                return;

            foreach (var @interface in type.AllInterfaces)
            {
                if (!IsReadOnlyQueryPortType(@interface) ||
                    SymbolEqualityComparer.Default.Equals(@interface, _readOnlyQueryPort))
                {
                    continue;
                }

                foreach (var interfaceMethod in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation ||
                        !implementation.Locations.Any(static location => location.IsInSource))
                    {
                        continue;
                    }

                    var normalized = NormalizeMethod(implementation);
                    if (normalized is not null)
                        _readOnlyQueryPortRoots.TryAdd(normalized, 0);
                }
            }
        }

        private void CaptureSecurityReadRoots(INamedTypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate || _securityReadInterfaces.IsDefaultOrEmpty)
                return;

            foreach (var @interface in type.AllInterfaces)
            {
                if (!_securityReadInterfaces.Any(candidate =>
                        SymbolEqualityComparer.Default.Equals(@interface, candidate)))
                {
                    continue;
                }

                foreach (var interfaceMethod in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation)
                        continue;

                    var normalized = NormalizeMethod(implementation);
                    if (normalized is not null)
                        _securityReadRoots.TryAdd(normalized, 0);
                }
            }
        }

        private static bool IsQueryHandlerInterface(INamedTypeSymbol type)
        {
            var namespaceName = type.ContainingNamespace.ToDisplayString();
            return (type.Name == "IQueryHandler" && namespaceName == "IIoT.SharedKernel.Messaging") ||
                   (type.Name == "IRequestHandler" && namespaceName == "MediatR");
        }

        private bool IsAiReadRequest(ITypeSymbol type)
        {
            return ImplementsOpenGeneric(type, _aiReadRequest);
        }

        private int CountRequestKinds(ITypeSymbol type)
        {
            var count = 0;
            if (ImplementsOpenGeneric(type, _humanRequest))
                count++;
            if (ImplementsOpenGeneric(type, _deviceRequest))
                count++;
            if (ImplementsOpenGeneric(type, _anonymousBootstrapRequest))
                count++;
            if (ImplementsOpenGeneric(type, _publicRequest))
                count++;
            if (ImplementsOpenGeneric(type, _aiReadRequest))
                count++;
            return count;
        }

        private void AnalyzeDatabaseTypeUse(SymbolAnalysisContext context, ISymbol owner, ITypeSymbol type)
        {
            if (!ShouldEnforceDatabaseOwner(owner) || !TryFindDatabaseApiType(type, out var databaseType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                CloudArchitectureDiagnostics.DatabaseOwner,
                GetSourceLocation(owner),
                owner.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                databaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private static bool TryFindDatabaseApiType(ITypeSymbol type, out ITypeSymbol databaseType)
        {
            if (IsDatabaseApiType(type))
            {
                databaseType = type;
                return true;
            }

            if (type is IArrayTypeSymbol array)
                return TryFindDatabaseApiType(array.ElementType, out databaseType);

            if (type is ITypeParameterSymbol parameter)
            {
                foreach (var constraint in parameter.ConstraintTypes)
                {
                    if (TryFindDatabaseApiType(constraint, out databaseType))
                        return true;
                }
            }

            if (type is INamedTypeSymbol namedType)
            {
                foreach (var argument in namedType.TypeArguments)
                {
                    if (TryFindDatabaseApiType(argument, out databaseType))
                        return true;
                }
            }

            databaseType = type;
            return false;
        }

        private bool IsDirectWriteSink(
            IInvocationOperation invocation,
            IMethodSymbol caller,
            IMethodSymbol method)
        {
            if (RepositoryWriteMethods.Contains(method.Name) && IsRepositoryType(method.ContainingType))
                return true;

            if (DatabaseWriteMethods.Contains(method.Name) &&
                IsDatabaseApiType(method.ContainingType) &&
                !IsDapperParameterBagMutation(method))
                return true;

            if (RawDatabaseAccessMethods.Contains(method.Name) && IsDatabaseApiType(method.ContainingType))
                return !(IsDapperCommandDefinitionInvocation(invocation) &&
                         IsDirectReadOnlyQueryPortImplementation(caller)) &&
                       !HasCompileTimeReadOnlySql(invocation);

            if ((method.Name == "SaveChanges" || method.Name == "SaveChangesAsync") &&
                method.ContainingAssembly.Name.StartsWith("IIoT.", StringComparison.Ordinal))
            {
                return true;
            }

            if (_command is not null && (method.Name == "Send" || method.Name == "Publish"))
            {
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Value.Type is not null && ImplementsOpenGeneric(argument.Value.Type, _command))
                        return true;
                }
            }

            return false;
        }

        private bool IsDirectReadOnlyQueryPortImplementation(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (containingType is null)
                return false;

            foreach (var @interface in containingType.AllInterfaces)
            {
                if (!IsReadOnlyQueryPortType(@interface))
                    continue;

                foreach (var interfaceMethod in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (containingType.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol implementation &&
                        SymbolEqualityComparer.Default.Equals(NormalizeMethod(implementation), NormalizeMethod(method)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsDapperParameterBagMutation(IMethodSymbol method)
        {
            return string.Equals(method.Name, "Add", StringComparison.Ordinal) &&
                   string.Equals(method.ContainingAssembly.Name, "Dapper", StringComparison.Ordinal) &&
                   string.Equals(
                       method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                       "Dapper.DynamicParameters",
                       StringComparison.Ordinal);
        }

        private static bool IsDapperCommandDefinitionInvocation(IInvocationOperation invocation)
        {
            return invocation.Arguments.Any(static argument =>
                string.Equals(
                    UnwrapConversion(argument.Value).Type?
                        .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    "Dapper.CommandDefinition",
                    StringComparison.Ordinal));
        }

        private static bool HasCompileTimeReadOnlySql(IInvocationOperation invocation)
        {
            var sqlArgument = invocation.Arguments.FirstOrDefault(static argument =>
                string.Equals(argument.Parameter?.Name, "sql", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument.Parameter?.Name, "commandText", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument.Parameter?.Name, "query", StringComparison.OrdinalIgnoreCase));
            sqlArgument ??= invocation.Arguments.FirstOrDefault(static argument =>
                UnwrapConversion(argument.Value).Type?.SpecialType == SpecialType.System_String);

            if (sqlArgument is null ||
                !TryGetConstantString(sqlArgument.Value, out var sql) ||
                !TryGetSqlCode(sql, out var code))
            {
                return false;
            }

            code = code.Trim();
            if (code.Length == 0)
                return false;

            var semicolon = code.IndexOf(';');
            if (semicolon >= 0)
            {
                if (semicolon != code.Length - 1 || code.IndexOf(';', semicolon + 1) >= 0)
                    return false;
                code = code.Substring(0, semicolon).TrimEnd();
            }

            if (!ReadOnlySqlStart.IsMatch(code) ||
                SqlWriteOrDdlKeyword.IsMatch(code) ||
                ContainsUnprovenSqlFunction(code))
                return false;

            return !code.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                   SqlSelectKeyword.IsMatch(code);
        }

        private static bool ContainsUnprovenSqlFunction(string code)
        {
            foreach (Match match in SqlFunctionCall.Matches(code))
            {
                var name = Regex.Replace(match.Groups["name"].Value, @"\s+", string.Empty);
                if (SqlStructuralParentheses.Contains(name))
                    continue;

                // Schema-qualified functions and every unrecognised function fail closed:
                // PostgreSQL permits user-defined functions to mutate state even in SELECT.
                if (name.IndexOf('.') >= 0 ||
                    name.IndexOf('"') >= 0 ||
                    !ProvenReadOnlySqlFunctions.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }

        private static IOperation UnwrapConversion(IOperation operation)
        {
            while (operation is IConversionOperation conversion)
                operation = conversion.Operand;
            return operation;
        }

        private static bool TryGetConstantString(IOperation operation, out string value)
        {
            operation = UnwrapConversion(operation);
            if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string constant)
            {
                value = constant;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool TryGetSqlCode(string sql, out string code)
        {
            var builder = new StringBuilder(sql.Length);
            var state = SqlLexicalState.Code;
            for (var index = 0; index < sql.Length; index++)
            {
                var current = sql[index];
                var next = index + 1 < sql.Length ? sql[index + 1] : '\0';
                switch (state)
                {
                    case SqlLexicalState.Code:
                        if (current == '-' && next == '-')
                        {
                            builder.Append("  ");
                            index++;
                            state = SqlLexicalState.LineComment;
                        }
                        else if (current == '/' && next == '*')
                        {
                            builder.Append("  ");
                            index++;
                            state = SqlLexicalState.BlockComment;
                        }
                        else if (current == '\'')
                        {
                            builder.Append(' ');
                            state = SqlLexicalState.StringLiteral;
                        }
                        else if (current == '"')
                        {
                            builder.Append('"');
                            state = SqlLexicalState.QuotedIdentifier;
                        }
                        else if (current == '[')
                        {
                            builder.Append(' ');
                            state = SqlLexicalState.BracketIdentifier;
                        }
                        else
                        {
                            builder.Append(current);
                        }
                        break;
                    case SqlLexicalState.LineComment:
                        builder.Append(current is '\r' or '\n' ? current : ' ');
                        if (current is '\r' or '\n')
                            state = SqlLexicalState.Code;
                        break;
                    case SqlLexicalState.BlockComment:
                        if (current == '*' && next == '/')
                        {
                            builder.Append("  ");
                            index++;
                            state = SqlLexicalState.Code;
                        }
                        else
                        {
                            builder.Append(current is '\r' or '\n' ? current : ' ');
                        }
                        break;
                    case SqlLexicalState.StringLiteral:
                        builder.Append(' ');
                        if (current == '\'' && next == '\'')
                        {
                            builder.Append(' ');
                            index++;
                        }
                        else if (current == '\'')
                        {
                            state = SqlLexicalState.Code;
                        }
                        break;
                    case SqlLexicalState.QuotedIdentifier:
                        if (current == '"' && next == '"')
                        {
                            builder.Append("qq");
                            index++;
                        }
                        else if (current == '"')
                        {
                            builder.Append('"');
                            state = SqlLexicalState.Code;
                        }
                        else
                        {
                            builder.Append(current is '\r' or '\n' ? current : 'q');
                        }
                        break;
                    case SqlLexicalState.BracketIdentifier:
                        builder.Append(' ');
                        if (current == ']' && next == ']')
                        {
                            builder.Append(' ');
                            index++;
                        }
                        else if (current == ']')
                        {
                            state = SqlLexicalState.Code;
                        }
                        break;
                }
            }

            code = builder.ToString();
            return state is SqlLexicalState.Code or SqlLexicalState.LineComment;
        }

        private enum SqlLexicalState
        {
            Code,
            LineComment,
            BlockComment,
            StringLiteral,
            QuotedIdentifier,
            BracketIdentifier
        }

        private bool IsRepositoryType(INamedTypeSymbol? type)
        {
            if (type is null)
                return false;

            if (IsRepositoryDefinition(type.OriginalDefinition))
                return true;

            return type.AllInterfaces.Any(@interface => IsRepositoryDefinition(@interface.OriginalDefinition));
        }

        private void AnalyzeAiReadWritePaths(CompilationAnalysisContext context)
        {
            AnalyzeCallPaths(
                context,
                _aiReadHandlerRoots.Keys,
                CloudArchitectureDiagnostics.AiReadWritePath,
                TryResolveSink);
            AnalyzeCallPaths(
                context,
                _readOnlyQueryPortRoots.Keys,
                CloudArchitectureDiagnostics.AiReadWritePath,
                TryResolveSink);
        }

        private bool TryResolveSink(
            InvocationEdge edge,
            HashSet<IMethodSymbol> visited,
            out string sink)
        {
            if (IsUnclassifiedCrossProjectInterfaceDispatch(edge))
            {
                sink = "unclassified cross-project interface dispatch: " + edge.TargetDisplay;
                return true;
            }

            return TryResolvePathSink(edge, visited, static item => item.IsDirectWriteSink, out sink);
        }

        private bool IsUnclassifiedCrossProjectInterfaceDispatch(InvocationEdge edge)
        {
            var targetType = edge.Target.ContainingType;
            return !edge.IsDirectWriteSink &&
                   targetType?.TypeKind == TypeKind.Interface &&
                   targetType.ContainingAssembly.Name.StartsWith("IIoT.", StringComparison.Ordinal) &&
                   !_callGraph.ContainsKey(edge.Target) &&
                   !IsReadOnlyQueryPortType(targetType);
        }

        private bool IsReadOnlyQueryPortType(INamedTypeSymbol? type)
        {
            if (type is null || _readOnlyQueryPort is null)
                return false;

            var directlyMarked = SymbolEqualityComparer.Default.Equals(type, _readOnlyQueryPort) ||
                                 SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _readOnlyQueryPort) ||
                                 type.Interfaces.Any(@interface =>
                                     SymbolEqualityComparer.Default.Equals(@interface, _readOnlyQueryPort));
            if (!directlyMarked)
                return false;

            // A mixed read/write port must never inherit read-only trust through a read base.
            return !type.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(static method =>
                    RepositoryWriteMethods.Contains(method.Name) ||
                    DatabaseWriteMethods.Contains(method.Name));
        }

        private void AnalyzeSecurityReadCachePaths(CompilationAnalysisContext context)
        {
            if (_cacheService is null)
                return;

            AnalyzeCallPaths(
                context,
                _securityReadRoots.Keys,
                CloudArchitectureDiagnostics.SecurityReadCachePath,
                TryResolveCacheSink);
        }

        private void AnalyzeCallPaths(
            CompilationAnalysisContext context,
            IEnumerable<IMethodSymbol> roots,
            DiagnosticDescriptor descriptor,
            SinkResolver resolveSink)
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in roots.OrderBy(
                         static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                         StringComparer.Ordinal))
            {
                if (!_callGraph.TryGetValue(root, out var rootEdges))
                    continue;

                foreach (var edge in rootEdges.OrderBy(static item => item.Location.SourceSpan.Start))
                {
                    if (!resolveSink(
                            edge,
                            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { root },
                            out var sink))
                    {
                        continue;
                    }

                    var key = root.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" +
                              edge.Location.SourceTree?.FilePath + ":" + edge.Location.SourceSpan.Start;
                    if (!reported.Add(key))
                        continue;

                    context.ReportDiagnostic(Diagnostic.Create(
                        descriptor,
                        edge.Location,
                        root.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        edge.TargetDisplay,
                        sink));
                }
            }
        }

        private bool TryResolveCacheSink(
            InvocationEdge edge,
            HashSet<IMethodSymbol> visited,
            out string sink)
        {
            return TryResolvePathSink(
                edge,
                visited,
                item => item.IsUnresolvedDynamic || IsCacheServiceType(item.Target.ContainingType),
                out sink);
        }

        private bool TryResolvePathSink(
            InvocationEdge edge,
            HashSet<IMethodSymbol> visited,
            Func<InvocationEdge, bool> isSink,
            out string sink)
        {
            if (isSink(edge))
            {
                sink = edge.TargetDisplay;
                return true;
            }

            if (!visited.Add(edge.Target) || !_callGraph.TryGetValue(edge.Target, out var children))
            {
                sink = string.Empty;
                return false;
            }

            foreach (var child in children.OrderBy(static item => item.Location.SourceSpan.Start))
            {
                if (TryResolvePathSink(child, visited, isSink, out sink))
                    return true;
            }

            sink = string.Empty;
            return false;
        }

        private bool IsCacheServiceType(INamedTypeSymbol? type)
        {
            if (type is null || _cacheService is null)
                return false;

            return SymbolEqualityComparer.Default.Equals(type, _cacheService) ||
                   SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _cacheService) ||
                   type.AllInterfaces.Any(@interface =>
                       SymbolEqualityComparer.Default.Equals(@interface, _cacheService));
        }

        private void AnalyzeLayerDependencies(CompilationAnalysisContext context)
        {
            if (_layer == CloudLayer.Unknown)
            {
                if (IsUnclassifiedCloudProductionAssembly(_assemblyName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        CloudArchitectureDiagnostics.LayerDependency,
                        Location.None,
                        _assemblyName,
                        "<unclassified IIoT production layer>"));
                }
                return;
            }
            if (_layer == CloudLayer.Host)
                return;

            foreach (var reference in _compilation.ReferencedAssemblyNames
                         .OrderBy(static identity => identity.Name, StringComparer.Ordinal))
            {
                var referencedLayer = ClassifyAssembly(reference.Name);
                if (!IsForbiddenReference(_layer, referencedLayer))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    CloudArchitectureDiagnostics.LayerDependency,
                    Location.None,
                    _assemblyName,
                    reference.Name));
            }
        }

        private void AnalyzeProductionTestReferences(CompilationAnalysisContext context)
        {
            if (IsTestOnlyAssembly(_assemblyName) ||
                _assemblyName.StartsWith("IIoT.CloudPlatform.Analyzer", StringComparison.Ordinal))
                return;

            foreach (var reference in _compilation.ReferencedAssemblyNames
                         .Where(static identity => IsTestOnlyAssembly(identity.Name))
                         .OrderBy(static identity => identity.Name, StringComparer.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CloudArchitectureDiagnostics.ProductionTestReference,
                    Location.None,
                    _assemblyName,
                    reference.Name));
            }
        }

        private static bool IsForbiddenReference(CloudLayer source, CloudLayer target)
        {
            return source switch
            {
                CloudLayer.Shared => target is CloudLayer.Core or CloudLayer.Service or CloudLayer.Infrastructure or CloudLayer.Host,
                CloudLayer.Core => target is CloudLayer.Service or CloudLayer.Infrastructure or CloudLayer.Host,
                CloudLayer.Service => target is CloudLayer.Infrastructure or CloudLayer.Host,
                CloudLayer.Infrastructure => target == CloudLayer.Host,
                _ => false
            };
        }

        private static CloudLayer ClassifyAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName) || IsTestOnlyAssembly(assemblyName) ||
                assemblyName.StartsWith("IIoT.CloudPlatform.Analyzer", StringComparison.Ordinal))
            {
                return CloudLayer.Unknown;
            }

            if (assemblyName == "IIoT.SharedKernel" ||
                assemblyName.StartsWith("IIoT.SharedKernel.", StringComparison.Ordinal))
            {
                return CloudLayer.Shared;
            }

            if (assemblyName.StartsWith("IIoT.Core.", StringComparison.Ordinal))
                return CloudLayer.Core;

            if (StartsWithAny(
                    assemblyName,
                    "IIoT.Services.",
                    "IIoT.EmployeeService",
                    "IIoT.IdentityService",
                    "IIoT.MasterDataService",
                    "IIoT.ProductionService"))
            {
                return CloudLayer.Service;
            }

            if (StartsWithAny(
                    assemblyName,
                    "IIoT.Dapper",
                    "IIoT.EntityFrameworkCore",
                    "IIoT.EventBus",
                    "IIoT.Infrastructure"))
            {
                return CloudLayer.Infrastructure;
            }

            if (StartsWithAny(
                    assemblyName,
                    "IIoT.AppHost",
                    "IIoT.DataWorker",
                    "IIoT.Gateway",
                    "IIoT.HttpApi",
                    "IIoT.MigrationWorkApp",
                    "IIoT.ServiceDefaults"))
            {
                return CloudLayer.Host;
            }

            return CloudLayer.Unknown;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
        }

        private bool ShouldEnforceDatabaseOwner(ISymbol containingSymbol)
        {
            if (_layer is CloudLayer.Unknown or CloudLayer.Infrastructure)
                return false;

            if (_databaseAllowedProjects.Contains(_assemblyName))
                return false;

            var containingType = containingSymbol.ContainingType;
            if (containingType is null)
                return true;

            var typeName = containingType
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            return !_databaseAllowedTypes.Contains(_assemblyName + "::" + typeName);
        }

        private static ImmutableHashSet<string> ReadOptionSet(
            AnalyzerConfigOptionsProvider provider,
            string key)
        {
            if (!provider.GlobalOptions.TryGetValue(key, out var rawValue) ||
                string.IsNullOrWhiteSpace(rawValue))
            {
                return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
            }

            return rawValue
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .ToImmutableHashSet(StringComparer.Ordinal);
        }

        private static bool IsTestOnlyAssembly(string assemblyName)
        {
            return string.Equals(
                       assemblyName,
                       "IIoT.CloudPlatform.PortFakes",
                       StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.IndexOf(".Tests.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   assemblyName.EndsWith(".Testing", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.IndexOf(".Testing.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   assemblyName.IndexOf("TestKit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   assemblyName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("Moq", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDatabaseApiType(ITypeSymbol? type)
        {
            if (type is null)
                return false;

            var assemblyName = type.ContainingAssembly?.Name ?? string.Empty;
            if (assemblyName == "Dapper" ||
                assemblyName.StartsWith("Npgsql", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                return true;
            }

            return EnumerateTypeHierarchy(type).Any(static candidate =>
            {
                var metadataName = candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return metadataName == "global::System.Data.IDbConnection" ||
                       metadataName == "global::System.Data.IDbCommand" ||
                       metadataName == "global::System.Data.Common.DbConnection" ||
                       metadataName == "global::System.Data.Common.DbCommand";
            });
        }

        private static IEnumerable<ITypeSymbol> EnumerateTypeHierarchy(ITypeSymbol type)
        {
            for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
                yield return current;

            if (type is INamedTypeSymbol namedType)
            {
                foreach (var @interface in namedType.AllInterfaces)
                    yield return @interface;
            }
        }

        private static bool Implements(ITypeSymbol type, INamedTypeSymbol interfaceType)
        {
            if (type is not INamedTypeSymbol namedType)
                return false;

            if (SymbolEqualityComparer.Default.Equals(namedType, interfaceType) ||
                SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, interfaceType))
            {
                return true;
            }

            return namedType.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(@interface, interfaceType) ||
                SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, interfaceType));
        }

        private static bool ImplementsOpenGeneric(ITypeSymbol type, INamedTypeSymbol? openGeneric)
        {
            if (openGeneric is null)
                return false;

            if (type is ITypeParameterSymbol parameter)
                return parameter.ConstraintTypes.Any(constraint => ImplementsOpenGeneric(constraint, openGeneric));

            if (type is not INamedTypeSymbol namedType)
                return false;

            if (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, openGeneric))
                return true;

            return namedType.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, openGeneric));
        }

        private static IMethodSymbol? NormalizeMethod(IMethodSymbol? method)
        {
            if (method is null)
                return null;

            return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
        }

        private static IMethodSymbol? GetOperationCaller(IOperation operation, ISymbol? containingSymbol)
        {
            for (var current = operation.Parent; current is not null; current = current.Parent)
            {
                if (current is IAnonymousFunctionOperation anonymousFunction)
                    return NormalizeMethod(anonymousFunction.Symbol);
            }

            return NormalizeMethod(containingSymbol as IMethodSymbol);
        }

        private static Location GetSourceLocation(ISymbol symbol)
        {
            return symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
        }

        private sealed class InvocationEdge
        {
            internal InvocationEdge(
                IMethodSymbol target,
                Location location,
                bool isDirectWriteSink,
                string targetDisplay,
                bool isUnresolvedDynamic = false)
            {
                Target = target;
                Location = location;
                IsDirectWriteSink = isDirectWriteSink;
                TargetDisplay = targetDisplay;
                IsUnresolvedDynamic = isUnresolvedDynamic;
            }

            internal IMethodSymbol Target { get; }
            internal Location Location { get; }
            internal bool IsDirectWriteSink { get; }
            internal string TargetDisplay { get; }
            internal bool IsUnresolvedDynamic { get; }
        }

        private sealed class DelegateBinding
        {
            private DelegateBinding(IMethodSymbol? target, ISymbol? storage)
            {
                Target = target;
                Storage = storage;
            }

            internal IMethodSymbol? Target { get; }
            internal ISymbol? Storage { get; }
            internal static DelegateBinding ForTarget(IMethodSymbol target) => new(target, null);
            internal static DelegateBinding ForStorage(ISymbol storage) => new(null, storage);
            internal static DelegateBinding Unresolved { get; } = new(null, null);
        }

        private sealed class StoredDelegateInvocation
        {
            internal StoredDelegateInvocation(IMethodSymbol caller, ISymbol storage, Location location)
            {
                Caller = caller;
                Storage = storage;
                Location = location;
            }

            internal IMethodSymbol Caller { get; }
            internal ISymbol Storage { get; }
            internal Location Location { get; }
        }

        private sealed class DelegateArgumentFlow
        {
            internal DelegateArgumentFlow(
                IMethodSymbol caller,
                IMethodSymbol target,
                IParameterSymbol parameter,
                IOperation actual,
                Location location,
                bool externalConsumer)
            {
                Caller = caller;
                Target = target;
                Parameter = parameter;
                Actual = actual;
                Location = location;
                ExternalConsumer = externalConsumer;
            }

            internal IMethodSymbol Caller { get; }
            internal IMethodSymbol Target { get; }
            internal IParameterSymbol Parameter { get; }
            internal IOperation Actual { get; }
            internal Location Location { get; }
            internal bool ExternalConsumer { get; }
        }

        private static bool IsUnclassifiedCloudProductionAssembly(string assemblyName)
        {
            return assemblyName.StartsWith("IIoT.", StringComparison.Ordinal) &&
                   !IsTestOnlyAssembly(assemblyName) &&
                   !assemblyName.StartsWith("IIoT.CloudPlatform.Analyzer", StringComparison.Ordinal);
        }

        private delegate bool SinkResolver(
            InvocationEdge edge,
            HashSet<IMethodSymbol> visited,
            out string sink);
    }
}
