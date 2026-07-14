using System.Collections.Concurrent;
using System.Collections.Immutable;
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
            "ExecuteNonQueryAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            CloudArchitectureDiagnostics.LayerDependency,
            CloudArchitectureDiagnostics.AggregateBoundary,
            CloudArchitectureDiagnostics.DatabaseOwner,
            CloudArchitectureDiagnostics.AiReadWritePath,
            CloudArchitectureDiagnostics.AiReadAuthorization,
            CloudArchitectureDiagnostics.ProductionTestReference);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
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
        context.RegisterOperationAction(state.AnalyzeObjectCreation, OperationKind.ObjectCreation);
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
        private readonly ImmutableHashSet<string> _databaseAllowedProjects;
        private readonly ImmutableHashSet<string> _databaseAllowedTypes;
        private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<InvocationEdge>> _callGraph =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _aiReadHandlerRoots =
            new(SymbolEqualityComparer.Default);

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
            CaptureAiReadHandlerRoots(type);
            CaptureInterfaceDispatch(type);
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

            var caller = NormalizeMethod(context.ContainingSymbol as IMethodSymbol);
            var target = NormalizeMethod(invocation.TargetMethod);
            if (caller is not null && target is not null)
            {
                var edge = new InvocationEdge(
                    target,
                    invocation.Syntax.GetLocation(),
                    IsDirectWriteSink(invocation, target),
                    target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>()).Add(edge);
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
            AnalyzeLayerDependencies(context);
            AnalyzeProductionTestReferences(context);
            AnalyzeAiReadWritePaths(context);
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

        private bool IsDirectWriteSink(IInvocationOperation invocation, IMethodSymbol method)
        {
            if (RepositoryWriteMethods.Contains(method.Name) && IsRepositoryType(method.ContainingType))
                return true;

            if (DatabaseWriteMethods.Contains(method.Name) && IsDatabaseApiType(method.ContainingType))
                return true;

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
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in _aiReadHandlerRoots.Keys.OrderBy(
                         static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                         StringComparer.Ordinal))
            {
                if (!_callGraph.TryGetValue(root, out var rootEdges))
                    continue;

                foreach (var edge in rootEdges.OrderBy(static item => item.Location.SourceSpan.Start))
                {
                    if (!TryResolveSink(edge, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { root }, out var sink))
                        continue;

                    var key = root.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" +
                              edge.Location.SourceTree?.FilePath + ":" + edge.Location.SourceSpan.Start;
                    if (!reported.Add(key))
                        continue;

                    context.ReportDiagnostic(Diagnostic.Create(
                        CloudArchitectureDiagnostics.AiReadWritePath,
                        edge.Location,
                        root.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        edge.TargetDisplay,
                        sink));
                }
            }
        }

        private bool TryResolveSink(
            InvocationEdge edge,
            HashSet<IMethodSymbol> visited,
            out string sink)
        {
            if (edge.IsDirectWriteSink)
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
                if (TryResolveSink(child, visited, out sink))
                    return true;
            }

            sink = string.Empty;
            return false;
        }

        private void AnalyzeLayerDependencies(CompilationAnalysisContext context)
        {
            if (_layer == CloudLayer.Unknown || _layer == CloudLayer.Host)
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
            return assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
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
            if (openGeneric is null || type is not INamedTypeSymbol namedType)
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
                string targetDisplay)
            {
                Target = target;
                Location = location;
                IsDirectWriteSink = isDirectWriteSink;
                TargetDisplay = targetDisplay;
            }

            internal IMethodSymbol Target { get; }
            internal Location Location { get; }
            internal bool IsDirectWriteSink { get; }
            internal string TargetDisplay { get; }
        }
    }
}
