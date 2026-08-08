using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructのシンボル解析: struct自体の検証、各<c>Update</c>オーバーロードの
/// パラメータ列からそのオーバーロードが対象とするComponent集合を読み取る。属性が存在しない設計
/// (ADR-0032)のため<c>ForAttributeWithMetadataName</c>は使えず、<see cref="StaticArchetypeGenerator"/>
/// 側で<c>AllInterfaces</c>をFQN文字列比較して検出する。ランタイム型(<c>Entity</c>/<c>State</c>等)へは
/// 完全修飾名の文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisからCSharpGameEngine.StaticArchetype
/// へのアセンブリ参照は発生させない(ADR-0001、既存ジェネレータと同じ方針)。
/// </summary>
internal static class StaticArchetypeAnalyzer
{
    internal const string SystemInterfaceFullyQualifiedName = "global::ISystem";
    private const string EntityFullyQualifiedName = "global::Entity";
    private const string StateFullyQualifiedName = "global::State";

    internal static StaticArchetypeSystemResult Analyze(INamedTypeSymbol systemSymbol)
    {
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (systemSymbol.TypeKind != TypeKind.Struct)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.SystemMustBeStruct, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        if (systemSymbol.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.SystemMustNotBeGeneric, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        if (systemSymbol.ContainingType is not null)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.SystemMustNotBeNested, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        List<(int MemberIndex, IMethodSymbol Method)> updateMethods = [];
        int memberIndex = 0;

        foreach (ISymbol member in systemSymbol.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, Name: "Update" } method)
            {
                updateMethods.Add((memberIndex, method));
            }

            memberIndex++;
        }

        if (updateMethods.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.SystemHasNoRecognizedUpdateOverload, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        List<StaticArchetypeUpdateOverloadModel> overloadModels = [];

        foreach ((int index, IMethodSymbol method) in updateMethods)
        {
            StaticArchetypeUpdateOverloadModel? model = AnalyzeOverload(method, index, diagnostics);

            if (model is { } value)
            {
                overloadModels.Add(value);
            }
        }

        return new StaticArchetypeSystemResult(
            systemSymbol.ContainingNamespace.IsGlobalNamespace ? null : systemSymbol.ContainingNamespace.ToDisplayString(),
            systemSymbol.Name,
            [.. overloadModels],
            diagnostics.ToImmutable());
    }

    private static StaticArchetypeUpdateOverloadModel? AnalyzeOverload(IMethodSymbol method, int memberIndex, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        bool isValid = true;
        Location methodLocation = method.Locations.FirstOrDefault() ?? Location.None;

        if (method.IsStatic)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.UpdateMethodMustBeInstance, methodLocation, method.Name));
            isValid = false;
        }

        if (method.DeclaredAccessibility == Accessibility.Private)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.UpdateMethodMustNotBePrivate, methodLocation, method.Name));
            isValid = false;
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.UpdateMethodMustReturnBool, methodLocation, method.Name, method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            isValid = false;
        }

        if (method.IsGenericMethod)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.UpdateMethodMustNotBeGeneric, methodLocation, method.Name));
            isValid = false;
        }

        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        bool hasLeadingEntity = false;
        string entityParameterName = string.Empty;
        int componentStart = 0;

        if (parameters.Length > 0)
        {
            IParameterSymbol first = parameters[0];
            string firstTypeFqn = first.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (firstTypeFqn == EntityFullyQualifiedName)
            {
                if (first.RefKind != RefKind.None)
                {
                    diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.LeadingEntityParameterMustBeByValueFirst, methodLocation, method.Name));
                    isValid = false;
                }

                hasLeadingEntity = true;
                entityParameterName = first.Name;
                componentStart = 1;
            }
        }

        bool trailingStateOk = parameters.Length > componentStart
            && parameters[parameters.Length - 1].RefKind == RefKind.Ref
            && parameters[parameters.Length - 1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == StateFullyQualifiedName;

        if (!trailingStateOk)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.UpdateMethodMustEndWithRefState, methodLocation, method.Name));
            isValid = false;
        }

        int componentEndExclusive = trailingStateOk ? parameters.Length - 1 : parameters.Length;
        List<StaticArchetypeComponentParameterModel> components = [];
        Dictionary<string, IParameterSymbol> seenComponentTypes = new(StringComparer.Ordinal);

        for (int i = componentStart; i < componentEndExclusive; i++)
        {
            IParameterSymbol parameter = parameters[i];
            string parameterTypeFqn = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (parameterTypeFqn == EntityFullyQualifiedName || parameterTypeFqn == StateFullyQualifiedName)
            {
                diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.EntityOrStateMustNotBeUsedAsComponent, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            if (parameter.RefKind != RefKind.Ref)
            {
                diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.ComponentParameterMustBeRef, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                isValid = false;
                continue;
            }

            if (!IsNonNullableStruct(parameter.Type))
            {
                diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.ComponentTypeMustBeNonNullableStruct, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            if (seenComponentTypes.ContainsKey(parameterTypeFqn))
            {
                diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.DuplicateComponentParameter, parameter.Locations.FirstOrDefault(), method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            seenComponentTypes.Add(parameterTypeFqn, parameter);
            components.Add(new StaticArchetypeComponentParameterModel(parameter.Name, parameterTypeFqn));
        }

        if (components.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(StaticArchetypeDiagnostics.UpdateMethodMustHaveAtLeastOneComponentParameter, methodLocation, method.Name));
            isValid = false;
        }

        if (!isValid)
        {
            return null;
        }

        return new StaticArchetypeUpdateOverloadModel(hasLeadingEntity, entityParameterName, [.. components], memberIndex);
    }

    private static bool IsNonNullableStruct(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Struct)
        {
            return false;
        }

        return type is not INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T };
    }
}
