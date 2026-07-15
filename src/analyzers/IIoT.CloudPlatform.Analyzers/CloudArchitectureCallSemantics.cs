using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IIoT.CloudPlatform.Analyzers;

/// <summary>
/// Shared Roslyn call semantics used by both the diagnostic analyzer and the
/// cross-assembly effect-summary generator. Keeping this resolution in one
/// place prevents a compiler-synthesized call from being safe in one path and
/// unsafe in the other.
/// </summary>
internal static class CloudArchitectureCallSemantics
{
    internal static ImmutableArray<IMethodSymbol> GetImplicitOperationTargets(IOperation operation)
    {
        var targets = CreateMethodSet();
        switch (operation)
        {
            case IObjectCreationOperation creation:
                Add(targets, creation.Constructor);
                break;
            case IPropertyReferenceOperation property:
                var (read, write) = GetPropertyAccess(property);
                if (read)
                    Add(targets, property.Property.GetMethod);
                if (write)
                    Add(targets, property.Property.SetMethod);
                break;
            case IFieldReferenceOperation field when field.Field.IsStatic:
                foreach (var staticConstructor in field.Field.ContainingType.StaticConstructors)
                    Add(targets, staticConstructor);
                break;
            case IEventAssignmentOperation eventAssignment
                when eventAssignment.EventReference is IEventReferenceOperation eventReference:
                Add(
                    targets,
                    eventAssignment.Adds
                        ? eventReference.Event.AddMethod
                        : eventReference.Event.RemoveMethod);
                break;
            case IBinaryOperation binary:
                Add(targets, binary.OperatorMethod);
                break;
            case IUnaryOperation unary:
                Add(targets, unary.OperatorMethod);
                break;
            case IIncrementOrDecrementOperation increment:
                Add(targets, increment.OperatorMethod);
                break;
            case ICompoundAssignmentOperation compound:
                Add(targets, compound.OperatorMethod);
                break;
            case IConversionOperation conversion:
                Add(targets, conversion.OperatorMethod);
                break;
        }

        return targets.ToImmutableArray();
    }

    internal static ImmutableArray<IMethodSymbol> GetImplicitSyntaxTargets(
        SemanticModel semanticModel,
        SyntaxNode syntax,
        CancellationToken cancellationToken)
    {
        var targets = CreateMethodSet();
        switch (syntax)
        {
            case CommonForEachStatementSyntax forEach:
                var forEachInfo = semanticModel.GetForEachStatementInfo(forEach);
                Add(targets, forEachInfo.GetEnumeratorMethod);
                Add(targets, forEachInfo.MoveNextMethod);
                Add(targets, forEachInfo.CurrentProperty?.GetMethod);
                Add(targets, forEachInfo.DisposeMethod);
                AddAwaitTargets(targets, forEachInfo.MoveNextAwaitableInfo);
                AddAwaitTargets(targets, forEachInfo.DisposeAwaitableInfo);
                Add(targets, forEachInfo.ElementConversion.MethodSymbol);
                Add(targets, forEachInfo.CurrentConversion.MethodSymbol);
                if (forEach is ForEachVariableStatementSyntax variableForEach)
                    AddDeconstructionTargets(targets, semanticModel.GetDeconstructionInfo(variableForEach));
                break;
            case AwaitExpressionSyntax awaitExpression:
                AddAwaitTargets(targets, semanticModel.GetAwaitExpressionInfo(awaitExpression));
                break;
            case AssignmentExpressionSyntax assignment
                when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                AddDeconstructionTargets(targets, semanticModel.GetDeconstructionInfo(assignment));
                break;
            case UsingStatementSyntax usingStatement:
                AddUsingDisposeTargets(
                    targets,
                    semanticModel,
                    usingStatement.Declaration,
                    usingStatement.Expression,
                    usingStatement.AwaitKeyword.RawKind != 0,
                    cancellationToken);
                if (usingStatement.AwaitKeyword.RawKind != 0)
                    AddAwaitTargets(targets, semanticModel.GetAwaitExpressionInfo(usingStatement));
                break;
            case LocalDeclarationStatementSyntax localDeclaration
                when localDeclaration.UsingKeyword.RawKind != 0:
                AddUsingDisposeTargets(
                    targets,
                    semanticModel,
                    localDeclaration.Declaration,
                    expression: null,
                    localDeclaration.AwaitKeyword.RawKind != 0,
                    cancellationToken);
                if (localDeclaration.AwaitKeyword.RawKind != 0)
                    AddAwaitTargets(targets, semanticModel.GetAwaitExpressionInfo(localDeclaration));
                break;
        }

        return targets.ToImmutableArray();
    }

    internal static ImmutableArray<IMethodSymbol> GetOperationCallers(
        IOperation operation,
        ISymbol? containingSymbol)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
                return One(NormalizeMethod(anonymousFunction.Symbol));
            if (current is ILocalFunctionOperation localFunction)
                return One(NormalizeMethod(localFunction.Symbol));
        }

        if (containingSymbol is IMethodSymbol method)
            return One(NormalizeMethod(method));

        if (containingSymbol is IFieldSymbol field)
            return GetInitializerCallers(field.ContainingType, field.IsStatic);

        if (containingSymbol is IEventSymbol @event)
            return GetInitializerCallers(@event.ContainingType, @event.IsStatic);

        if (containingSymbol is IPropertySymbol property)
        {
            if (IsPropertyInitializerOperation(operation))
                return GetInitializerCallers(property.ContainingType, property.IsStatic);

            return One(NormalizeMethod(property.GetMethod ?? property.SetMethod));
        }

        return ImmutableArray<IMethodSymbol>.Empty;
    }

    internal static ImmutableArray<IMethodSymbol> GetInitializerCallers(
        INamedTypeSymbol type,
        bool isStatic)
    {
        var methods = CreateMethodSet();
        if (isStatic)
        {
            foreach (var constructor in type.StaticConstructors)
                Add(methods, constructor);

            // Roslyn normally exposes an implicit .cctor for a static initializer.
            // If a future compiler does not, conservatively attach the initializer
            // to every callable member that can trigger type initialization.
            if (methods.Count == 0)
            {
                foreach (var method in EnumerateCallableMethods(type))
                    Add(methods, method);
            }
        }
        else
        {
            foreach (var constructor in type.InstanceConstructors)
                Add(methods, constructor);
        }

        return methods.ToImmutableArray();
    }

    internal static IEnumerable<IMethodSymbol> EnumerateCallableMethods(INamedTypeSymbol type)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            yield return method;

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.GetMethod is not null)
                yield return property.GetMethod;
            if (property.SetMethod is not null)
                yield return property.SetMethod;
        }

        foreach (var @event in type.GetMembers().OfType<IEventSymbol>())
        {
            if (@event.AddMethod is not null)
                yield return @event.AddMethod;
            if (@event.RemoveMethod is not null)
                yield return @event.RemoveMethod;
            if (@event.RaiseMethod is not null)
                yield return @event.RaiseMethod;
        }
    }

    internal static bool IsOpenDispatch(IMethodSymbol method)
    {
        method = NormalizeMethod(method)!;
        var containingType = method.ContainingType;
        if (!IsExternallyVisible(containingType))
            return false;

        if (containingType.TypeKind == TypeKind.Interface)
            return true;

        if ((!method.IsVirtual && !method.IsAbstract && !method.IsOverride) ||
            method.IsSealed || containingType.IsSealed)
        {
            return false;
        }

        return method.DeclaredAccessibility is Accessibility.Public or
            Accessibility.Protected or
            Accessibility.ProtectedOrInternal;
    }

    /// <summary>
    /// Returns true when a same-compilation call crosses a boundary whose
    /// implementation cannot be inspected. Such a call cannot be summarized
    /// as read-only merely because it contributes no Roslyn operation edges.
    /// </summary>
    internal static bool IsUnresolvedSourceBoundary(IMethodSymbol method)
    {
        method = NormalizeMethod(method)!;
        if (method.IsExtern)
            return true;

        if (HasExecutableSourceBody(method) ||
            method.PartialImplementationPart is { } implementation &&
            HasExecutableSourceBody(implementation))
        {
            return false;
        }

        if (method.PartialDefinitionPart is not null ||
            method.PartialImplementationPart is not null)
        {
            return true;
        }

        if (method.IsImplicitlyDeclared)
            return false;

        if (method.MethodKind == MethodKind.Constructor &&
            method.DeclaringSyntaxReferences.Any(static reference =>
                reference.GetSyntax() is TypeDeclarationSyntax))
        {
            // Primary-constructor bodies are represented by base-constructor
            // and initializer operations attached to this constructor.
            return false;
        }

        // Semicolon accessors on a concrete source type are compiler-backed
        // storage operations. They are safe boundaries; their initializers and
        // containing type constructors are analyzed separately.
        if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet &&
            method.ContainingType.TypeKind != TypeKind.Interface &&
            !method.IsAbstract &&
            method.DeclaringSyntaxReferences.Any(static reference =>
                reference.GetSyntax() is AccessorDeclarationSyntax accessor &&
                accessor.SemicolonToken.RawKind != 0))
        {
            return false;
        }

        // Abstract/interface members are handled by dispatch closure and open
        // dispatch checks. The concrete unauditable source boundaries are
        // extern and incomplete partial methods above.
        return false;
    }

    internal static bool HasExecutableSourceBody(IMethodSymbol method)
    {
        if (method.AssociatedSymbol is IPropertySymbol property)
        {
            foreach (var syntaxReference in property.DeclaringSyntaxReferences)
            {
                var propertySyntax = syntaxReference.GetSyntax();
                if (propertySyntax is PropertyDeclarationSyntax propertyDeclaration &&
                    propertyDeclaration.ExpressionBody is not null ||
                    propertySyntax is IndexerDeclarationSyntax indexerDeclaration &&
                    indexerDeclaration.ExpressionBody is not null)
                {
                    return true;
                }

                if (propertySyntax is BasePropertyDeclarationSyntax withAccessors &&
                    withAccessors.AccessorList?.Accessors.Any(accessor =>
                        accessor.Keyword.ValueText == (method.MethodKind == MethodKind.PropertyGet ? "get" : "set") &&
                        (accessor.Body is not null || accessor.ExpressionBody is not null)) == true)
                {
                    return true;
                }
            }
        }

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            if (syntax is MethodDeclarationSyntax declaration &&
                (declaration.Body is not null || declaration.ExpressionBody is not null))
            {
                return true;
            }

            if (syntax is LocalFunctionStatementSyntax localFunction &&
                (localFunction.Body is not null || localFunction.ExpressionBody is not null))
            {
                return true;
            }

            if (syntax is BaseMethodDeclarationSyntax baseMethod &&
                (baseMethod.Body is not null || baseMethod.ExpressionBody is not null))
            {
                return true;
            }

            if (syntax is AccessorDeclarationSyntax accessor &&
                (accessor.Body is not null || accessor.ExpressionBody is not null))
            {
                return true;
            }

            if (syntax is PropertyDeclarationSyntax propertyDeclaration && propertyDeclaration.ExpressionBody is not null)
                return true;

            if (syntax is IndexerDeclarationSyntax indexer && indexer.ExpressionBody is not null)
                return true;
        }

        return false;
    }

    private static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (
                    Accessibility.Public or
                    Accessibility.Protected or
                    Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    internal static IMethodSymbol? NormalizeMethod(IMethodSymbol? method) =>
        method is null ? null : method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;

    private static void AddUsingDisposeTargets(
        HashSet<IMethodSymbol> targets,
        SemanticModel semanticModel,
        VariableDeclarationSyntax? declaration,
        ExpressionSyntax? expression,
        bool isAsync,
        CancellationToken cancellationToken)
    {
        if (expression is not null)
            AddDisposeTargets(targets, semanticModel.GetTypeInfo(expression, cancellationToken).Type, isAsync);

        if (declaration is null)
            return;

        foreach (var variable in declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol local)
                AddDisposeTargets(targets, local.Type, isAsync);
        }
    }

    private static void AddDisposeTargets(
        HashSet<IMethodSymbol> targets,
        ITypeSymbol? resourceType,
        bool isAsync)
    {
        if (resourceType is ITypeParameterSymbol parameter)
        {
            foreach (var constraint in parameter.ConstraintTypes)
                AddDisposeTargets(targets, constraint, isAsync);
            return;
        }

        if (resourceType is not INamedTypeSymbol namedType)
            return;

        var methodName = isAsync ? "DisposeAsync" : "Dispose";
        for (var current = namedType; current is not null; current = current.BaseType)
        {
            var declared = current.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Where(static method => method.Parameters.Length == 0)
                .ToArray();
            foreach (var method in declared)
                Add(targets, method);
            if (declared.Length > 0)
                break;
        }

        foreach (var @interface in namedType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(methodName)
                         .OfType<IMethodSymbol>()
                         .Where(static method => method.Parameters.Length == 0))
            {
                Add(targets, namedType.FindImplementationForInterfaceMember(member) as IMethodSymbol ?? member);
            }
        }
    }

    private static void AddAwaitTargets(HashSet<IMethodSymbol> targets, AwaitExpressionInfo info)
    {
        Add(targets, info.RuntimeAwaitMethod);
        Add(targets, info.GetAwaiterMethod);
        Add(targets, info.IsCompletedProperty?.GetMethod);
        Add(targets, info.GetResultMethod);
    }

    private static void AddDeconstructionTargets(
        HashSet<IMethodSymbol> targets,
        DeconstructionInfo info)
    {
        Add(targets, info.Method);
        Add(targets, info.Conversion?.MethodSymbol);
        foreach (var nested in info.Nested)
            AddDeconstructionTargets(targets, nested);
    }

    private static (bool Read, bool Write) GetPropertyAccess(IPropertyReferenceOperation reference)
    {
        IOperation current = reference;
        while (current.Parent is IConversionOperation conversion &&
               ReferenceEquals(conversion.Operand, current))
        {
            current = conversion;
        }

        return current.Parent switch
        {
            ISimpleAssignmentOperation assignment when Contains(assignment.Target, reference) => (false, true),
            ICompoundAssignmentOperation assignment when Contains(assignment.Target, reference) => (true, true),
            IIncrementOrDecrementOperation increment when Contains(increment.Target, reference) => (true, true),
            IArgumentOperation argument when argument.Parameter?.RefKind == RefKind.Out => (false, true),
            IArgumentOperation argument when argument.Parameter?.RefKind == RefKind.Ref => (true, true),
            _ => (true, false)
        };
    }

    private static bool IsPropertyInitializerOperation(IOperation operation)
    {
        for (var current = operation; current is not null; current = current.Parent)
        {
            if (current is IPropertyInitializerOperation)
                return true;
        }

        return false;
    }

    private static bool Contains(IOperation root, IOperation candidate) =>
        ReferenceEquals(root, candidate) ||
        root.Descendants().Any(operation => ReferenceEquals(operation, candidate));

    private static HashSet<IMethodSymbol> CreateMethodSet() =>
        new(SymbolEqualityComparer.Default);

    private static void Add(HashSet<IMethodSymbol> methods, IMethodSymbol? method)
    {
        method = NormalizeMethod(method);
        if (method is not null)
            methods.Add(method);
    }

    private static ImmutableArray<IMethodSymbol> One(IMethodSymbol? method) =>
        method is null ? ImmutableArray<IMethodSymbol>.Empty : ImmutableArray.Create(method);
}
