using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructのシンボル解析。属性を一切使わない設計(ADR-0033)のため、
/// パラメータの役割はすべて構造的に判別する:
/// <list type="number">
/// <item>先頭・値渡し・型が<c>Entity</c> → Entityスロット(任意)</item>
/// <item>末尾・<c>ref State</c> → Stateスロット(<c>Update</c>では必須)</item>
/// <item>それ以外で<c>ref</c>の非nullable struct → Component</item>
/// <item>それ以外(<c>ref</c>でないもの。structは<c>in</c>、参照型は値渡し) → Context</item>
/// </list>
/// Componentは<c>ref</c>必須・Contextは<c>ref</c>禁止なので、ref-kindだけで曖昧性なく判別できる。
/// ランタイム型へは完全修飾名の文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisから
/// CSharpGameEngine.Entitiesへのアセンブリ参照は発生させない(ADR-0001)。
/// </summary>
internal static class GameSystemAnalyzer
{
    internal const string SystemInterfaceFullyQualifiedName = "global::CSharpGameEngine.Entities.ISystem";
    internal const string EntityFullyQualifiedName = "global::CSharpGameEngine.Entities.Entity";
    internal const string StateFullyQualifiedName = "global::CSharpGameEngine.Entities.State";

    private const string UpdateMethodName = "Update";
    private const string SpawnMethodName = "Spawn";

    internal static GameSystemResult Analyze(INamedTypeSymbol systemSymbol)
    {
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (systemSymbol.TypeKind != TypeKind.Struct)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemMustBeStruct, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        if (systemSymbol.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemMustNotBeGeneric, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        if (systemSymbol.ContainingType is not null)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemMustNotBeNested, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        List<IMethodSymbol> methods = [];

        foreach (ISymbol member in systemSymbol.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method
                && (method.Name == UpdateMethodName || method.Name == SpawnMethodName))
            {
                methods.Add(method);
            }
        }

        if (methods.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemHasNoRecognizedMethod, systemSymbol.Locations.FirstOrDefault(), systemSymbol.Name));
        }

        // 同一struct内のオーバーロード順序は、実行順序(ADR-0033決定事項1: 記述順)に直結するため
        // ソース上の宣言位置で並べる。GetMembers()の順序はメタデータ順で保証が弱いので明示的に整列する。
        methods.Sort(static (a, b) => GetDeclarationPosition(a).CompareTo(GetDeclarationPosition(b)));

        List<GameSystemMethodModel> methodModels = [];

        foreach (IMethodSymbol method in methods)
        {
            GameSystemMethodModel? model = AnalyzeMethod(method, diagnostics);

            if (model is { } value)
            {
                methodModels.Add(value);
            }
        }

        Location typeLocation = systemSymbol.Locations.FirstOrDefault() ?? Location.None;

        return new GameSystemResult(
            systemSymbol.ContainingNamespace.IsGlobalNamespace ? null : systemSymbol.ContainingNamespace.ToDisplayString(),
            systemSymbol.Name,
            [.. methodModels],
            diagnostics.ToImmutable(),
            typeLocation.SourceTree?.FilePath ?? string.Empty,
            typeLocation.SourceSpan.Start);
    }

    private static int GetDeclarationPosition(ISymbol symbol)
    {
        Location location = symbol.Locations.FirstOrDefault() ?? Location.None;
        return location.SourceSpan.Start;
    }

    private static GameSystemMethodModel? AnalyzeMethod(IMethodSymbol method, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        bool isSpawn = method.Name == SpawnMethodName;
        GameSystemMethodRole role = isSpawn ? GameSystemMethodRole.SpawnOnly : GameSystemMethodRole.Dispatch;
        bool isValid = true;
        Location methodLocation = method.Locations.FirstOrDefault() ?? Location.None;

        if (method.IsStatic)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustBeInstance, methodLocation, method.Name));
            isValid = false;
        }

        if (method.DeclaredAccessibility == Accessibility.Private)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustNotBePrivate, methodLocation, method.Name));
            isValid = false;
        }

        if (method.IsGenericMethod)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustNotBeGeneric, methodLocation, method.Name));
            isValid = false;
        }

        if (isSpawn)
        {
            if (!method.ReturnsVoid)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SpawnMethodMustReturnVoid, methodLocation, method.Name, method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                isValid = false;
            }
        }
        else if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustReturnBool, methodLocation, method.Name, method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            isValid = false;
        }

        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        bool hasLeadingEntity = false;
        int scanStart = 0;
        int scanEndExclusive = parameters.Length;

        if (parameters.Length > 0)
        {
            IParameterSymbol first = parameters[0];

            if (TypeFqn(first) == EntityFullyQualifiedName)
            {
                if (isSpawn)
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SpawnMethodMustNotDeclareEntityOrState, methodLocation, method.Name));
                    isValid = false;
                }
                else
                {
                    if (first.RefKind != RefKind.None)
                    {
                        diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.LeadingEntityParameterMustBeByValueFirst, methodLocation, method.Name));
                        isValid = false;
                    }

                    hasLeadingEntity = true;
                }

                scanStart = 1;
            }
        }

        bool hasTrailingState = parameters.Length > scanStart
            && parameters[parameters.Length - 1].RefKind == RefKind.Ref
            && TypeFqn(parameters[parameters.Length - 1]) == StateFullyQualifiedName;

        if (isSpawn)
        {
            if (hasTrailingState)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SpawnMethodMustNotDeclareEntityOrState, methodLocation, method.Name));
                isValid = false;
                scanEndExclusive = parameters.Length - 1;
            }
        }
        else if (hasTrailingState)
        {
            scanEndExclusive = parameters.Length - 1;
        }
        else
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustEndWithRefState, methodLocation, method.Name));
            isValid = false;
        }

        List<GameSystemComponentParameterModel> components = [];
        List<GameSystemContextParameterModel> contexts = [];
        List<GameSystemParameterSlot> callOrder = [];
        HashSet<string> seenComponentTypes = new(StringComparer.Ordinal);
        HashSet<string> seenContextTypes = new(StringComparer.Ordinal);

        if (hasLeadingEntity)
        {
            callOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Entity, 0));
        }

        for (int i = scanStart; i < scanEndExclusive; i++)
        {
            IParameterSymbol parameter = parameters[i];
            string parameterTypeFqn = TypeFqn(parameter);

            if (parameterTypeFqn == EntityFullyQualifiedName || parameterTypeFqn == StateFullyQualifiedName)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EntityOrStateMustNotBeUsedAsComponent, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            if (parameter.RefKind == RefKind.Ref)
            {
                if (!IsNonNullableStruct(parameter.Type))
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentTypeMustBeNonNullableStruct, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameterTypeFqn));
                    isValid = false;
                    continue;
                }

                if (!seenComponentTypes.Add(parameterTypeFqn))
                {
                    diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateComponentParameter, parameter.Locations.FirstOrDefault(), method.Name, parameterTypeFqn));
                    isValid = false;
                    continue;
                }

                callOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Component, components.Count));
                components.Add(new GameSystemComponentParameterModel(parameter.Name, parameterTypeFqn));
                continue;
            }

            // ref以外はContext。ただし`out`/`ref readonly`のような中途半端な渡し方は弾く。
            bool isReferenceType = parameter.Type.IsReferenceType;
            bool refKindOk = isReferenceType ? parameter.RefKind == RefKind.None : parameter.RefKind == RefKind.In;

            if (!refKindOk)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ContextParameterMustBeInOrByValue, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name, parameter.RefKind.ToString()));
                isValid = false;
                continue;
            }

            if (!seenContextTypes.Add(parameterTypeFqn))
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateContextParameter, parameter.Locations.FirstOrDefault(), method.Name, parameterTypeFqn));
                isValid = false;
                continue;
            }

            callOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Context, contexts.Count));
            contexts.Add(new GameSystemContextParameterModel(parameter.Name, parameterTypeFqn, isReferenceType));
        }

        if (hasTrailingState && !isSpawn)
        {
            callOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.State, 0));
        }

        if (components.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustHaveAtLeastOneComponentParameter, methodLocation, method.Name));
            isValid = false;
        }

        if (!isValid)
        {
            return null;
        }

        return new GameSystemMethodModel(role, hasLeadingEntity, [.. components], [.. contexts], [.. callOrder], GetDeclarationPosition(method));
    }

    private static string TypeFqn(IParameterSymbol parameter) =>
        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool IsNonNullableStruct(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Struct)
        {
            return false;
        }

        return type is not INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T };
    }
}
