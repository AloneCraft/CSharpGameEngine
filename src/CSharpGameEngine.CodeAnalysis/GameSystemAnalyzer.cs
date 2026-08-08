using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[GameSystem]</c>が付いた静的クラスのシンボル解析: クラス自体の検証、各<c>[Update]</c>メソッドの
/// パラメータからQuery条件(All/None)とRead/Writeアクセスを推論する。ランタイム型
/// (<c>CSharpGameEngine.Entities.Entity</c>等)へは完全修飾名の文字列としてのみ参照し、
/// CSharpGameEngine.CodeAnalysisからCSharpGameEngine.Entitiesへのアセンブリ参照は発生させない
/// (ADR-0001、既存2ジェネレータと同じ方針)。
/// </summary>
internal static class GameSystemAnalyzer
{
    private const string ContextAttributeFullyQualifiedName = "global::CSharpGameEngine.Entities.ContextAttribute";
    private const string CurrentEntityAttributeFullyQualifiedName = "global::CSharpGameEngine.Entities.CurrentEntityAttribute";
    private const string CommandsAttributeFullyQualifiedName = "global::CSharpGameEngine.Entities.CommandsAttribute";
    private const string UpdateAttributeFullyQualifiedName = "global::CSharpGameEngine.Entities.UpdateAttribute";
    private const string WithoutAttributeFullyQualifiedName = "global::CSharpGameEngine.Entities.WithoutAttribute";
    private const string EntityFullyQualifiedName = "global::CSharpGameEngine.Entities.Entity";
    private const string EntityCommandBufferFullyQualifiedName = "global::CSharpGameEngine.Entities.EntityCommandBuffer";

    internal static GameSystemClassResult Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken cancellationToken)
    {
        INamedTypeSymbol classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (!classSymbol.IsStatic || classSymbol.TypeKind != TypeKind.Class)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.GameSystemMustBeStaticClass, classSymbol.Locations.FirstOrDefault(), classSymbol.Name));
        }

        if (classSymbol.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.GameSystemMustNotBeGeneric, classSymbol.Locations.FirstOrDefault(), classSymbol.Name));
        }

        if (classSymbol.ContainingType is not null)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.GameSystemMustNotBeNested, classSymbol.Locations.FirstOrDefault(), classSymbol.Name));
        }

        List<IMethodSymbol> updateMethods = [];
        List<(int MemberIndex, ISymbol Symbol)> members = [.. classSymbol.GetMembers().Select((m, i) => (i, m))];

        foreach ((int memberIndex, ISymbol member) in members)
        {
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                continue;
            }

            if (!HasAttribute(method.GetAttributes(), UpdateAttributeFullyQualifiedName))
            {
                continue;
            }

            updateMethods.Add(method);
        }

        Dictionary<string, IMethodSymbol> seenMethodNames = new(StringComparer.Ordinal);
        List<SystemMethodModel> methodModels = [];

        for (int memberIndex = 0; memberIndex < updateMethods.Count; memberIndex++)
        {
            IMethodSymbol method = updateMethods[memberIndex];

            if (seenMethodNames.TryGetValue(method.Name, out IMethodSymbol? _))
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateUpdateMethodName, method.Locations.FirstOrDefault(), classSymbol.Name, method.Name));
                continue;
            }

            seenMethodNames.Add(method.Name, method);

            SystemMethodModel? model = AnalyzeMethod(method, memberIndex, diagnostics);

            if (model is { } value)
            {
                methodModels.Add(value);
            }
        }

        return new GameSystemClassResult(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            [.. methodModels],
            diagnostics.ToImmutable());
    }

    private static SystemMethodModel? AnalyzeMethod(IMethodSymbol method, int memberIndex, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        bool isValid = true;
        Location methodLocation = method.Locations.FirstOrDefault() ?? Location.None;

        if (!method.IsStatic)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustBeStatic, methodLocation, method.Name));
            isValid = false;
        }

        if (!method.ReturnsVoid)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustReturnVoid, methodLocation, method.Name, method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            isValid = false;
        }

        if (method.IsGenericMethod)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustNotBeGeneric, methodLocation, method.Name));
            isValid = false;
        }

        if (method.DeclaredAccessibility == Accessibility.Private)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustNotBePrivate, methodLocation, method.Name));
            isValid = false;
        }

        List<ComponentParameterModel> componentParameters = [];
        List<ContextParameterModel> contextParameters = [];
        List<ParameterSlot> callOrder = [];
        bool usesCurrentEntity = false;
        bool usesCommands = false;
        Dictionary<string, IParameterSymbol> seenComponentTypes = new(StringComparer.Ordinal);
        HashSet<string> seenContextTypes = new(StringComparer.Ordinal);

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            ImmutableArray<AttributeData> attributes = parameter.GetAttributes();
            bool hasContext = HasAttribute(attributes, ContextAttributeFullyQualifiedName);
            bool hasCurrentEntity = HasAttribute(attributes, CurrentEntityAttributeFullyQualifiedName);
            bool hasCommands = HasAttribute(attributes, CommandsAttributeFullyQualifiedName);
            int roleCount = (hasContext ? 1 : 0) + (hasCurrentEntity ? 1 : 0) + (hasCommands ? 1 : 0);

            if (roleCount > 1)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ParameterMustNotCombineMultipleRoleAttributes, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                isValid = false;
                continue;
            }

            string parameterTypeFqn = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (hasCurrentEntity)
            {
                if (usesCurrentEntity)
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateSpecialParameter, parameter.Locations.FirstOrDefault(), method.Name, "[CurrentEntity]"));
                    isValid = false;
                    continue;
                }

                if (parameter.RefKind != RefKind.None || parameterTypeFqn != EntityFullyQualifiedName)
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.CurrentEntityParameterMustBeEntityByValue, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                    isValid = false;
                    continue;
                }

                usesCurrentEntity = true;
                callOrder.Add(new ParameterSlot(ParameterRole.CurrentEntity, 0));
                continue;
            }

            if (hasCommands)
            {
                if (usesCommands)
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateSpecialParameter, parameter.Locations.FirstOrDefault(), method.Name, "[Commands]"));
                    isValid = false;
                    continue;
                }

                if (parameter.RefKind != RefKind.None || parameterTypeFqn != EntityCommandBufferFullyQualifiedName)
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.CommandsParameterMustBeEntityCommandBufferByValue, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                    isValid = false;
                    continue;
                }

                usesCommands = true;
                callOrder.Add(new ParameterSlot(ParameterRole.Commands, 0));
                continue;
            }

            if (hasContext)
            {
                bool isReferenceType = parameter.Type.IsReferenceType;
                bool refKindOk = isReferenceType ? parameter.RefKind == RefKind.None : parameter.RefKind == RefKind.In;

                if (!refKindOk)
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ContextParameterMustBeInOrByValue, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                    isValid = false;
                    continue;
                }

                if (!seenContextTypes.Add(parameterTypeFqn))
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateSpecialParameter, parameter.Locations.FirstOrDefault(), method.Name, parameterTypeFqn));
                    isValid = false;
                    continue;
                }

                callOrder.Add(new ParameterSlot(ParameterRole.Context, contextParameters.Count));
                contextParameters.Add(new ContextParameterModel(parameter.Name, parameterTypeFqn, isReferenceType));
                continue;
            }

            if (parameter.RefKind != RefKind.Ref && parameter.RefKind != RefKind.In)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentParameterMustBeRefOrIn, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                isValid = false;
                continue;
            }

            if (!IsNonNullableStruct(parameter.Type))
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentTypeMustBeNonNullableStruct, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            if (seenComponentTypes.ContainsKey(parameterTypeFqn))
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateComponentParameter, parameter.Locations.FirstOrDefault(), method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            seenComponentTypes.Add(parameterTypeFqn, parameter);

            bool isTag = IsTagType(parameter.Type);

            if (isTag && parameter.RefKind == RefKind.Ref)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.TagComponentMustNotBeRef, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            ComponentAccess access = parameter.RefKind == RefKind.Ref ? ComponentAccess.Write : ComponentAccess.Read;
            callOrder.Add(new ParameterSlot(ParameterRole.Component, componentParameters.Count));
            componentParameters.Add(new ComponentParameterModel(parameter.Name, parameterTypeFqn, access, isTag));
        }

        if (componentParameters.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustHaveAtLeastOneComponentParameter, methodLocation, method.Name));
            isValid = false;
        }

        List<string> withoutComponentTypes = [];

        foreach (AttributeData attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != WithoutAttributeFullyQualifiedName)
            {
                continue;
            }

            ITypeSymbol? excludedType = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;
            string excludedTypeName = excludedType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "?";

            if (excludedType is null || !IsNonNullableStruct(excludedType) || seenComponentTypes.ContainsKey(excludedTypeName))
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.WithoutArgumentInvalid, methodLocation, method.Name, excludedTypeName));
                isValid = false;
                continue;
            }

            withoutComponentTypes.Add(excludedTypeName);
        }

        if (!isValid)
        {
            return null;
        }

        int order = GetUpdateOrder(method);

        List<string> readComponentTypes = [.. componentParameters.Where(p => p.Access == ComponentAccess.Read && !p.IsTag).Select(p => p.TypeFullyQualifiedName)];
        List<string> writeComponentTypes = [.. componentParameters.Where(p => p.Access == ComponentAccess.Write).Select(p => p.TypeFullyQualifiedName)];
        readComponentTypes.Sort(StringComparer.Ordinal);
        writeComponentTypes.Sort(StringComparer.Ordinal);

        return new SystemMethodModel(
            method.Name,
            order,
            memberIndex,
            methodLocation,
            [.. componentParameters],
            [.. contextParameters],
            [.. callOrder],
            usesCurrentEntity,
            usesCommands,
            [.. withoutComponentTypes],
            [.. readComponentTypes],
            [.. writeComponentTypes]);
    }

    private static int GetUpdateOrder(IMethodSymbol method)
    {
        AttributeData? updateAttribute = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == UpdateAttributeFullyQualifiedName);

        if (updateAttribute is null)
        {
            return 0;
        }

        foreach (KeyValuePair<string, TypedConstant> namedArgument in updateAttribute.NamedArguments)
        {
            if (namedArgument.Key == "Order" && namedArgument.Value.Value is int order)
            {
                return order;
            }
        }

        return 0;
    }

    private static bool HasAttribute(ImmutableArray<AttributeData> attributes, string fullyQualifiedName)
    {
        foreach (AttributeData attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullyQualifiedName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonNullableStruct(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Struct)
        {
            return false;
        }

        return type is not INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T };
    }

    private static bool IsTagType(ITypeSymbol type)
    {
        return !type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic);
    }
}
