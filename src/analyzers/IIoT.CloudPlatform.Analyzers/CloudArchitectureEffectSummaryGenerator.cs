using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace IIoT.CloudPlatform.Analyzers;

internal enum CloudArchitectureMethodEffect
{
    Safe,
    Write
}

internal sealed class CloudArchitectureAssemblyEffectSummary
{
    private CloudArchitectureAssemblyEffectSummary(
        bool valid,
        ImmutableDictionary<string, CloudArchitectureMethodEffect> effects,
        string failure)
    {
        Valid = valid;
        Effects = effects;
        Failure = failure;
    }

    internal bool Valid { get; }
    internal ImmutableDictionary<string, CloudArchitectureMethodEffect> Effects { get; }
    internal string Failure { get; }

    internal static CloudArchitectureAssemblyEffectSummary Read(
        IAssemblySymbol assembly,
        string? expectedSourceIdentity)
    {
        if (string.IsNullOrWhiteSpace(expectedSourceIdentity))
            return Invalid("unmanaged-reference");

        if (!TryGetUniqueMetadataValue(
                assembly,
                CloudArchitectureEffectSummaryFormat.ManifestKey,
                out var manifestValue))
        {
            return Invalid("missing-or-duplicate-manifest");
        }

        if (!TryGetUniqueMetadataValue(
                assembly,
                CloudArchitectureEffectSummaryFormat.SourceIdentityKey,
                out var sourceIdentity) ||
            !CloudArchitectureEffectSummaryFormat.IsDigest(sourceIdentity) ||
            !string.Equals(sourceIdentity, expectedSourceIdentity, StringComparison.Ordinal))
        {
            return Invalid("missing-duplicate-or-mismatched-source-identity");
        }

        var manifestParts = manifestValue.Split('|');
        if (manifestParts.Length != 3 ||
            !int.TryParse(
                manifestParts[0],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var schema) ||
            !int.TryParse(
                manifestParts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var expectedCount) ||
            schema != CloudArchitectureEffectSummaryFormat.SchemaVersion ||
            expectedCount < 0 ||
            string.IsNullOrWhiteSpace(manifestParts[2]))
        {
            return Invalid("invalid-manifest");
        }
        var expectedDigest = manifestParts[2];

        var builder = ImmutableDictionary.CreateBuilder<string, CloudArchitectureMethodEffect>(StringComparer.Ordinal);
        foreach (var attribute in assembly.GetAttributes().Where(static attribute => string.Equals(
                     attribute.AttributeClass?.ToDisplayString(),
                     CloudArchitectureEffectSummaryFormat.MetadataAttributeName,
                     StringComparison.Ordinal) &&
                     HasMetadataKey(attribute, CloudArchitectureEffectSummaryFormat.EffectKey)))
        {
            if (attribute.ConstructorArguments.Length != 2 ||
                attribute.ConstructorArguments[1].Value is not string entryValue)
            {
                return Invalid("invalid-or-duplicate-entry");
            }

            var separator = entryValue.LastIndexOf('\t');
            if (separator <= 0 || separator == entryValue.Length - 1)
                return Invalid("invalid-or-duplicate-entry");

            var methodId = entryValue.Substring(0, separator);
            var effectText = entryValue.Substring(separator + 1);
            if (
                string.IsNullOrWhiteSpace(methodId) ||
                !Enum.TryParse(effectText, ignoreCase: true, out CloudArchitectureMethodEffect effect) ||
                builder.ContainsKey(methodId))
            {
                return Invalid("invalid-or-duplicate-entry");
            }

            builder.Add(methodId, effect);
        }

        if (builder.Count != expectedCount ||
            !string.Equals(
                CloudArchitectureEffectSummaryFormat.ComputeDigest(builder),
                expectedDigest,
                StringComparison.Ordinal))
        {
            return Invalid("count-or-digest-mismatch");
        }

        return new CloudArchitectureAssemblyEffectSummary(true, builder.ToImmutable(), string.Empty);
    }

    private static CloudArchitectureAssemblyEffectSummary Invalid(string failure) =>
        new(false, ImmutableDictionary<string, CloudArchitectureMethodEffect>.Empty, failure);

    private static bool TryGetUniqueMetadataValue(
        IAssemblySymbol assembly,
        string key,
        out string value)
    {
        var attributes = assembly.GetAttributes()
            .Where(attribute => string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                CloudArchitectureEffectSummaryFormat.MetadataAttributeName,
                StringComparison.Ordinal) && HasMetadataKey(attribute, key))
            .ToArray();
        if (attributes.Length == 1 &&
            attributes[0].ConstructorArguments.Length == 2 &&
            attributes[0].ConstructorArguments[1].Value is string metadataValue)
        {
            value = metadataValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool HasMetadataKey(AttributeData attribute, string key) =>
        attribute.ConstructorArguments.Length == 2 &&
        string.Equals(attribute.ConstructorArguments[0].Value as string, key, StringComparison.Ordinal);
}

internal static class CloudArchitectureEffectSummaryFormat
{
    internal const int SchemaVersion = 2;
    internal const string MetadataAttributeName = "System.Reflection.AssemblyMetadataAttribute";
    internal const string ManifestKey = "IIoT.CloudArchitecture.EffectSummary.Manifest";
    internal const string EffectKey = "IIoT.CloudArchitecture.EffectSummary.Method";
    internal const string SourceIdentityKey = "IIoT.CloudArchitecture.EffectSummary.SourceIdentity";
    internal const string ManagedReferencesFileName = "CloudArchitectureManagedProjectReferences.txt";

    internal static string GetMethodId(IMethodSymbol method)
    {
        var normalized = method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
        return GetTypeId(normalized.ContainingType) + "::" +
               normalized.MetadataName + "`" +
               normalized.Arity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "(" +
               string.Join(",", normalized.Parameters.Select(static parameter =>
                   ((int)parameter.RefKind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   GetTypeId(parameter.Type))) + ")->" +
               GetTypeId(normalized.ReturnType);
    }

    internal static string GetFieldId(IFieldSymbol field)
    {
        var normalized = field.OriginalDefinition;
        return GetTypeId(normalized.ContainingType) + "::field:" +
               normalized.MetadataName + "->" +
               GetTypeId(normalized.Type);
    }

    private static string GetTypeId(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return GetTypeId(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
            case IPointerTypeSymbol pointer:
                return GetTypeId(pointer.PointedAtType) + "*";
            case ITypeParameterSymbol parameter:
                return (parameter.TypeParameterKind == TypeParameterKind.Method ? "!!" : "!") +
                       parameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case INamedTypeSymbol named:
                var prefix = named.ContainingType is not null
                    ? GetTypeId(named.ContainingType) + "+"
                    : named.ContainingNamespace.IsGlobalNamespace
                        ? string.Empty
                        : named.ContainingNamespace.ToDisplayString() + ".";
                var arguments = named.IsGenericType
                    ? "<" + string.Join(",", named.TypeArguments.Select(GetTypeId)) + ">"
                    : string.Empty;
                return prefix + named.MetadataName + arguments;
            case IDynamicTypeSymbol:
                return "System.Object";
            default:
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
    }

    internal static string ComputeDigest(IEnumerable<KeyValuePair<string, CloudArchitectureMethodEffect>> effects)
    {
        var canonical = string.Join(
            "\n",
            effects.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key + "\t" + pair.Value.ToString().ToLowerInvariant()));
        return ComputeSha256(canonical);
    }

    internal static string ComputeSourceIdentity(string projectIdentity)
    {
        var normalized = projectIdentity.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(static segment => segment == "..") ||
            !normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cloud architecture project identity must be a stable repository-relative csproj path.");
        }

        return ComputeSha256(normalized);
    }

    private static string ComputeSha256(string value)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var octet in hash)
            builder.Append(octet.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    internal static bool IsDigest(string value)
    {
        return value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

internal sealed class CloudArchitectureManagedReferenceCatalog
{
    private readonly ImmutableDictionary<string, string> _sourceIdentities;

    private CloudArchitectureManagedReferenceCatalog(
        bool valid,
        ImmutableDictionary<string, string> sourceIdentities)
    {
        Valid = valid;
        _sourceIdentities = sourceIdentities;
    }

    internal bool Valid { get; }

    internal static CloudArchitectureManagedReferenceCatalog Read(
        ImmutableArray<AdditionalText> additionalTexts,
        CancellationToken cancellationToken)
    {
        var catalogs = additionalTexts.Where(static text => string.Equals(
                Path.GetFileName(text.Path),
                CloudArchitectureEffectSummaryFormat.ManagedReferencesFileName,
                StringComparison.Ordinal))
            .ToArray();
        if (catalogs.Length != 1)
            return Invalid();

        var text = catalogs[0].GetText(cancellationToken);
        if (text is null)
            return Invalid();

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Lines)
        {
            var value = line.ToString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var parts = value.Split('\t');
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1]) ||
                string.IsNullOrWhiteSpace(parts[2]))
            {
                return Invalid();
            }

            string sourceIdentity;
            try
            {
                sourceIdentity = CloudArchitectureEffectSummaryFormat.ComputeSourceIdentity(parts[2]);
            }
            catch
            {
                return Invalid();
            }

            foreach (var referencePath in parts.Take(2).Where(static path =>
                         !string.IsNullOrWhiteSpace(path)))
            {
                string normalized;
                try
                {
                    normalized = Path.GetFullPath(referencePath).Replace('\\', '/');
                }
                catch
                {
                    return Invalid();
                }

                if (builder.TryGetValue(normalized, out var existing) &&
                    !string.Equals(existing, sourceIdentity, StringComparison.Ordinal))
                {
                    return Invalid();
                }

                builder[normalized] = sourceIdentity;
            }
        }

        return new CloudArchitectureManagedReferenceCatalog(true, builder.ToImmutable());
    }

    internal bool TryGetSourceIdentity(
        Compilation compilation,
        IAssemblySymbol assembly,
        out string sourceIdentity)
    {
        if (Valid)
        {
            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                if (reference.FilePath is null ||
                    compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol candidate ||
                    !SymbolEqualityComparer.Default.Equals(candidate, assembly))
                {
                    continue;
                }

                var normalized = Path.GetFullPath(reference.FilePath).Replace('\\', '/');
                if (_sourceIdentities.TryGetValue(normalized, out sourceIdentity!))
                    return true;
            }
        }

        sourceIdentity = string.Empty;
        return false;
    }

    private static CloudArchitectureManagedReferenceCatalog Invalid() =>
        new(false, ImmutableDictionary<string, string>.Empty);
}

[Generator(LanguageNames.CSharp)]
public sealed class CloudArchitectureEffectSummaryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var input = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(context.AdditionalTextsProvider.Collect());
        context.RegisterSourceOutput(
            input,
            static (productionContext, source) => Execute(
                productionContext,
                source.Left.Left,
                source.Left.Right,
                source.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider,
        ImmutableArray<AdditionalText> additionalTexts)
    {
        var assemblyName = compilation.AssemblyName ?? string.Empty;
        if (!assemblyName.StartsWith("IIoT.", StringComparison.Ordinal) ||
            assemblyName.StartsWith("IIoT.CloudPlatform.Analyzer", StringComparison.Ordinal))
        {
            return;
        }

        optionsProvider.GlobalOptions.TryGetValue(
            "build_property.CloudArchitectureProjectIdentity",
            out var projectIdentity);
        var sourceIdentity = string.IsNullOrWhiteSpace(projectIdentity)
            ? string.Empty
            : CloudArchitectureEffectSummaryFormat.ComputeSourceIdentity(projectIdentity!);
        var managedReferences = CloudArchitectureManagedReferenceCatalog.Read(
            additionalTexts,
            context.CancellationToken);
        var collector = new CloudArchitectureEffectCollector(
            compilation,
            managedReferences,
            context.CancellationToken);
        var effects = collector.Collect();
        var digest = CloudArchitectureEffectSummaryFormat.ComputeDigest(effects);
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        var manifest = CloudArchitectureEffectSummaryFormat.SchemaVersion.ToString(
                           System.Globalization.CultureInfo.InvariantCulture) + "|" +
                       effects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                       digest;
        source.AppendLine("[assembly: global::System.Reflection.AssemblyMetadataAttribute(" +
                          SymbolDisplay.FormatLiteral(CloudArchitectureEffectSummaryFormat.ManifestKey, quote: true) + ", " +
                          SymbolDisplay.FormatLiteral(manifest, quote: true) + ")]");
        source.AppendLine("[assembly: global::System.Reflection.AssemblyMetadataAttribute(" +
                          SymbolDisplay.FormatLiteral(
                              CloudArchitectureEffectSummaryFormat.SourceIdentityKey,
                              quote: true) + ", " +
                          SymbolDisplay.FormatLiteral(sourceIdentity, quote: true) + ")]");
        foreach (var pair in effects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            source.AppendLine("[assembly: global::System.Reflection.AssemblyMetadataAttribute(" +
                              SymbolDisplay.FormatLiteral(CloudArchitectureEffectSummaryFormat.EffectKey, quote: true) + ", " +
                              SymbolDisplay.FormatLiteral(
                                  pair.Key + "\t" + pair.Value.ToString().ToLowerInvariant(),
                                  quote: true) + ")]");
        }

        context.AddSource("CloudArchitectureEffectSummary.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }
}

internal sealed class CloudArchitectureEffectCollector
{
    private readonly Compilation _compilation;
    private readonly CloudArchitectureManagedReferenceCatalog _managedReferences;
    private readonly CancellationToken _cancellationToken;
    private readonly INamedTypeSymbol? _command;
    private readonly Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> _edges =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _directWrites = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IAssemblySymbol, CloudArchitectureAssemblyEffectSummary> _externalSummaries =
        new(SymbolEqualityComparer.Default);

    internal CloudArchitectureEffectCollector(
        Compilation compilation,
        CloudArchitectureManagedReferenceCatalog managedReferences,
        CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _managedReferences = managedReferences;
        _cancellationToken = cancellationToken;
        _command = compilation.GetTypeByMetadataName("IIoT.SharedKernel.Messaging.ICommand`1");
    }

    internal ImmutableDictionary<string, CloudArchitectureMethodEffect> Collect()
    {
        ScanOperations();
        CaptureDispatchAndTypeInitializerEdges();
        var builder = ImmutableDictionary.CreateBuilder<string, CloudArchitectureMethodEffect>(StringComparer.Ordinal);
        foreach (var method in EnumerateAssemblyMethods(_compilation.Assembly.GlobalNamespace)
                     .Where(static method => method.DeclaredAccessibility != Accessibility.Private ||
                                             method.MethodKind == MethodKind.StaticConstructor))
        {
            var normalized = NormalizeMethod(method)!;
            var effect = ResolvesWrite(
                normalized,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))
                ? CloudArchitectureMethodEffect.Write
                : CloudArchitectureMethodEffect.Safe;
            builder[CloudArchitectureEffectSummaryFormat.GetMethodId(normalized)] = effect;
        }

        foreach (var type in EnumerateAssemblyTypes(_compilation.Assembly.GlobalNamespace))
        {
            var staticInitializerWrites = type.StaticConstructors.Any(staticConstructor => ResolvesWrite(
                NormalizeMethod(staticConstructor)!,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(static field =>
                         field.IsStatic && field.DeclaredAccessibility != Accessibility.Private))
            {
                builder[CloudArchitectureEffectSummaryFormat.GetFieldId(field)] = staticInitializerWrites
                    ? CloudArchitectureMethodEffect.Write
                    : CloudArchitectureMethodEffect.Safe;
            }
        }

        return builder.ToImmutable();
    }

    private void ScanOperations()
    {
        foreach (var syntaxTree in _compilation.SyntaxTrees)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = _compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(_cancellationToken);
            foreach (var syntax in root.DescendantNodes())
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (IsOperationSyntax(syntax))
                {
                    var operation = semanticModel.GetOperation(syntax, _cancellationToken);
                    if (operation is not null)
                    {
                        var containingSymbol = semanticModel.GetEnclosingSymbol(
                            syntax.SpanStart,
                            _cancellationToken);
                        foreach (var candidateOperation in operation.DescendantsAndSelf())
                        {
                            var callers = CloudArchitectureCallSemantics.GetOperationCallers(
                                candidateOperation,
                                containingSymbol);
                            foreach (var caller in callers)
                                ScanOperation(caller, candidateOperation);
                        }
                    }
                }

                if (!IsCompilerImplicitSyntax(syntax))
                    continue;

                var implicitOperation = semanticModel.GetOperation(syntax, _cancellationToken);
                if (implicitOperation is null)
                    continue;

                var implicitCallers = CloudArchitectureCallSemantics.GetOperationCallers(
                    implicitOperation,
                    semanticModel.GetEnclosingSymbol(syntax.SpanStart, _cancellationToken));
                foreach (var target in CloudArchitectureCallSemantics.GetImplicitSyntaxTargets(
                             semanticModel,
                             syntax,
                             _cancellationToken))
                {
                    foreach (var caller in implicitCallers)
                        AddEdge(caller, target);
                }
            }
        }
    }

    private void ScanOperation(IMethodSymbol caller, IOperation operation)
    {
        foreach (var target in CloudArchitectureCallSemantics.GetImplicitOperationTargets(operation))
            AddEdge(caller, target);

        switch (operation)
        {
            case IInvocationOperation invocation:
                if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke ||
                    string.Equals(invocation.TargetMethod.Name, "DynamicInvoke", StringComparison.Ordinal))
                {
                    if (!TryAddDelegateEdges(caller, invocation.Instance))
                        MarkDirectWrite(caller);
                    return;
                }

                AddEdge(caller, invocation.TargetMethod);
                if (IsDirectWriteInvocation(invocation, caller))
                    MarkDirectWrite(caller);

                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter?.Type.TypeKind == TypeKind.Delegate &&
                        !TryAddDelegateEdges(caller, argument.Value))
                    {
                        // An unresolved callback crossing a call boundary is an
                        // unknown side effect and must fail closed.
                        MarkDirectWrite(caller);
                    }
                }
                return;
            case IDynamicInvocationOperation:
                MarkDirectWrite(caller);
                return;
            case IFieldReferenceOperation fieldReference
                when fieldReference.Field.IsStatic &&
                     !SymbolEqualityComparer.Default.Equals(
                         fieldReference.Field.ContainingAssembly,
                         _compilation.Assembly) &&
                     IsIIoTProductionAssembly(fieldReference.Field.ContainingAssembly.Name):
                if (IsUnsafeExternalFieldEffect(fieldReference.Field))
                    MarkDirectWrite(caller);
                return;
        }
    }

    private bool IsUnsafeExternalFieldEffect(IFieldSymbol field)
    {
        if (!_externalSummaries.TryGetValue(field.ContainingAssembly, out var summary))
        {
            summary = ReadExternalSummary(field.ContainingAssembly);
            _externalSummaries.Add(field.ContainingAssembly, summary);
        }

        return !summary.Valid ||
               !summary.Effects.TryGetValue(
                   CloudArchitectureEffectSummaryFormat.GetFieldId(field),
                   out var effect) ||
               effect == CloudArchitectureMethodEffect.Write;
    }

    private bool TryAddDelegateEdges(IMethodSymbol caller, IOperation? operation)
    {
        if (operation is null)
            return false;

        switch (operation)
        {
            case IConversionOperation conversion:
                return TryAddDelegateEdges(caller, conversion.Operand);
            case IDelegateCreationOperation delegateCreation:
                return TryAddDelegateEdges(caller, delegateCreation.Target);
            case IAnonymousFunctionOperation anonymousFunction:
                AddEdge(caller, anonymousFunction.Symbol);
                return true;
            case IMethodReferenceOperation methodReference:
                AddEdge(caller, methodReference.Method);
                return true;
            case IConditionalOperation conditional:
                return TryAddDelegateEdges(caller, conditional.WhenTrue) &
                       (conditional.WhenFalse is not null &&
                        TryAddDelegateEdges(caller, conditional.WhenFalse));
            case ICoalesceOperation coalesce:
                var leftResolved = coalesce.Value.ConstantValue is { HasValue: true, Value: null } ||
                                   TryAddDelegateEdges(caller, coalesce.Value);
                return leftResolved & TryAddDelegateEdges(caller, coalesce.WhenNull);
            default:
                return false;
        }
    }

    private void CaptureDispatchAndTypeInitializerEdges()
    {
        foreach (var type in EnumerateAssemblyTypes(_compilation.Assembly.GlobalNamespace))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            foreach (var @interface in type.AllInterfaces)
            {
                foreach (var interfaceMethod in EnumerateCallableMethods(@interface))
                {
                    if (type.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol implementation)
                        AddEdge(interfaceMethod, implementation);
                }
            }

            foreach (var method in EnumerateCallableMethods(type))
            {
                if (method.OverriddenMethod is not null)
                    AddEdge(method.OverriddenMethod, method);
            }

            foreach (var staticConstructor in type.StaticConstructors)
            {
                foreach (var method in EnumerateCallableMethods(type))
                {
                    if (!SymbolEqualityComparer.Default.Equals(
                            NormalizeMethod(method),
                            NormalizeMethod(staticConstructor)))
                    {
                        AddEdge(method, staticConstructor);
                    }
                }
            }
        }
    }

    private void MarkDirectWrite(IMethodSymbol method)
    {
        method = NormalizeMethod(method)!;
        _directWrites.Add(method);
    }

    private static bool IsOperationSyntax(SyntaxNode syntax) =>
        syntax is ExpressionSyntax or ConstructorInitializerSyntax;

    private static bool IsCompilerImplicitSyntax(SyntaxNode syntax) =>
        syntax is CommonForEachStatementSyntax or AwaitExpressionSyntax or UsingStatementSyntax ||
        syntax is LocalDeclarationStatementSyntax localDeclaration && localDeclaration.UsingKeyword.RawKind != 0 ||
        syntax is AssignmentExpressionSyntax assignment &&
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);

    private void AddEdge(IMethodSymbol caller, IMethodSymbol? target)
    {
        target = NormalizeMethod(target);
        if (target is null || SymbolEqualityComparer.Default.Equals(caller, target))
            return;
        if (!_edges.TryGetValue(caller, out var targets))
        {
            targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            _edges.Add(caller, targets);
        }
        targets.Add(target);
    }

    private bool ResolvesWrite(IMethodSymbol method, HashSet<IMethodSymbol> visited)
    {
        if (_directWrites.Contains(method) ||
            SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _compilation.Assembly) &&
            CloudArchitectureCallSemantics.IsUnresolvedSourceBoundary(method) &&
            !HasResolvedSourceImplementation(method))
            return true;
        if (!visited.Add(method) || !_edges.TryGetValue(method, out var targets))
            return false;

        foreach (var target in targets)
        {
            if (SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, _compilation.Assembly))
            {
                if (CloudArchitectureCallSemantics.IsOpenDispatch(target) &&
                    !IsReadOnlyQueryPortType(target.ContainingType))
                    return true;
                if (ResolvesWrite(target, visited))
                    return true;
                continue;
            }

            if (!IsIIoTProductionAssembly(target.ContainingAssembly.Name))
                continue;

            if (IsReadOnlyQueryPortType(target.ContainingType))
                continue;

            if (CloudArchitectureCallSemantics.IsOpenDispatch(target))
                return true;

            if (!_externalSummaries.TryGetValue(target.ContainingAssembly, out var summary))
            {
                summary = ReadExternalSummary(target.ContainingAssembly);
                _externalSummaries.Add(target.ContainingAssembly, summary);
            }

            if (!summary.Valid ||
                !summary.Effects.TryGetValue(CloudArchitectureEffectSummaryFormat.GetMethodId(target), out var effect) ||
                effect == CloudArchitectureMethodEffect.Write)
            {
                return true;
            }
        }

        return false;
    }

    private CloudArchitectureAssemblyEffectSummary ReadExternalSummary(IAssemblySymbol assembly)
    {
        var sourceIdentity = _managedReferences.TryGetSourceIdentity(
            _compilation,
            assembly,
            out var expectedSourceIdentity)
            ? expectedSourceIdentity
            : null;
        return CloudArchitectureAssemblyEffectSummary.Read(assembly, sourceIdentity);
    }

    private bool HasResolvedSourceImplementation(IMethodSymbol method)
    {
        return _edges.TryGetValue(method, out var targets) &&
               targets.Any(target =>
                   target.MethodKind != MethodKind.StaticConstructor &&
                   SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, _compilation.Assembly) &&
                   !CloudArchitectureCallSemantics.IsUnresolvedSourceBoundary(target));
    }

    private bool IsDirectWriteInvocation(IInvocationOperation invocation, IMethodSymbol caller)
    {
        return CloudArchitectureAnalyzer.IsDirectEffectSink(
            invocation,
            caller,
            IsDirectReadOnlyQueryPortImplementation,
            _command);
    }

    private bool IsDirectReadOnlyQueryPortImplementation(IMethodSymbol method)
    {
        var marker = _compilation.GetTypeByMetadataName("IIoT.SharedKernel.Architecture.IReadOnlyQueryPort");
        if (marker is null)
            return false;
        foreach (var @interface in method.ContainingType.AllInterfaces)
        {
            if (!@interface.Interfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, marker)))
                continue;
            foreach (var member in EnumerateCallableMethods(@interface))
            {
                if (method.ContainingType.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation &&
                    SymbolEqualityComparer.Default.Equals(NormalizeMethod(implementation), method))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool IsReadOnlyQueryPortType(INamedTypeSymbol? type)
    {
        var marker = _compilation.GetTypeByMetadataName(
            "IIoT.SharedKernel.Architecture.IReadOnlyQueryPort");
        if (type is null || marker is null)
            return false;

        var marked = SymbolEqualityComparer.Default.Equals(type, marker) ||
                     type.AllInterfaces.Any(candidate =>
                         SymbolEqualityComparer.Default.Equals(candidate, marker));
        if (!marked)
            return false;

        return !CloudArchitectureAnalyzer.HasWritableCapabilitySurface(type);
    }


    private static IEnumerable<IMethodSymbol> EnumerateAssemblyMethods(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            foreach (var method in EnumerateTypeMethods(type))
                yield return method;
        }
        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var method in EnumerateAssemblyMethods(child))
                yield return method;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateAssemblyTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            foreach (var current in EnumerateTypeAndNestedTypes(type))
                yield return current;
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in EnumerateAssemblyTypes(child))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNestedTypes(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var current in EnumerateTypeAndNestedTypes(nested))
                yield return current;
        }
    }

    private static IEnumerable<IMethodSymbol> EnumerateTypeMethods(INamedTypeSymbol type)
    {
        foreach (var method in EnumerateCallableMethods(type))
            yield return method;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var method in EnumerateTypeMethods(nested))
                yield return method;
        }
    }

    private static IEnumerable<IMethodSymbol> EnumerateCallableMethods(INamedTypeSymbol type)
    {
        return CloudArchitectureCallSemantics.EnumerateCallableMethods(type);
    }

    private static IMethodSymbol? NormalizeMethod(IMethodSymbol? method) =>
        method is null ? null : method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;

    private static bool IsIIoTProductionAssembly(string assemblyName) =>
        assemblyName.StartsWith("IIoT.", StringComparison.Ordinal) &&
        !assemblyName.EndsWith("Tests", StringComparison.Ordinal) &&
        !assemblyName.EndsWith("TestKit", StringComparison.Ordinal) &&
        !assemblyName.StartsWith("IIoT.CloudPlatform.Analyzer", StringComparison.Ordinal);
}
