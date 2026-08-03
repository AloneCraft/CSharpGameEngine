using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[EcsArchetype]</c>が付いた構造体のシンボル解析: partial/ジェネリック/ネスト検証、
/// コンポーネントプロパティの列挙・検証、完全修飾名によるOrdinal正規化、コンポーネント型重複検出を行う。
/// </summary>
internal static class EcsArchetypeAnalyzer
{
    private const string EcsReadOnlyAttributeFullyQualifiedName = "global::CSharpGameEngine.Ecs.EcsReadOnlyAttribute";

    internal static EcsArchetypeAnalysisResult Analyze(GeneratorAttributeSyntaxContext ctx)
    {
        INamedTypeSymbol structSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (ctx.TargetNode is StructDeclarationSyntax structDeclaration && !structDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.ArchetypeMustBePartial, structDeclaration.Identifier.GetLocation(), structSymbol.Name));
        }

        if (structSymbol.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.ArchetypeMustNotBeGeneric, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        if (structSymbol.ContainingType is not null)
        {
            diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.ArchetypeMustNotBeNested, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        List<IPropertySymbol> componentProperties = structSymbol.GetMembers().OfType<IPropertySymbol>().Where(p => p.IsPartialDefinition).ToList();

        if (componentProperties.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.ArchetypeMustHaveAtLeastOneComponent, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        List<EcsComponentPropertyModel> normalizedProperties = [];
        Dictionary<string, IPropertySymbol> seenComponentTypes = new(StringComparer.Ordinal);

        foreach (IPropertySymbol property in componentProperties)
        {
            Location propertyLocation = property.Locations.FirstOrDefault() ?? Location.None;

            if (property.RefKind != RefKind.Ref && property.RefKind != RefKind.In)
            {
                diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.PropertyMustReturnByRef, propertyLocation, property.Name));
                continue;
            }

            if (!IsNonNullableStruct(property.Type))
            {
                diagnostics.Add(Diagnostic.Create(
                    EcsArchetypeDiagnostics.ComponentTypeMustBeNonNullableStruct,
                    propertyLocation,
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            bool isReadOnly = property.RefKind == RefKind.In;
            bool hasEcsReadOnlyAttribute = property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EcsReadOnlyAttributeFullyQualifiedName);

            if (hasEcsReadOnlyAttribute && !isReadOnly)
            {
                diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.EcsReadOnlyRequiresRefReadonly, propertyLocation, property.Name));
            }

            if (isReadOnly && !hasEcsReadOnlyAttribute)
            {
                diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.RefReadonlyRequiresEcsReadOnly, propertyLocation, property.Name));
            }

            string componentTypeFullyQualifiedName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (seenComponentTypes.ContainsKey(componentTypeFullyQualifiedName))
            {
                diagnostics.Add(Diagnostic.Create(EcsArchetypeDiagnostics.DuplicateComponentType, propertyLocation, structSymbol.Name, property.Type.Name));
                continue;
            }

            seenComponentTypes.Add(componentTypeFullyQualifiedName, property);
            normalizedProperties.Add(new EcsComponentPropertyModel(
                property.Name,
                ToParameterName(property.Name),
                componentTypeFullyQualifiedName,
                isReadOnly));
        }

        normalizedProperties.Sort(static (a, b) => string.CompareOrdinal(a.ComponentTypeFullyQualifiedName, b.ComponentTypeFullyQualifiedName));

        return new EcsArchetypeAnalysisResult(
            structSymbol.ContainingNamespace.IsGlobalNamespace ? null : structSymbol.ContainingNamespace.ToDisplayString(),
            structSymbol.Name,
            [.. normalizedProperties],
            diagnostics.ToImmutable());
    }

    private static bool IsNonNullableStruct(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Struct)
        {
            return false;
        }

        return type is not INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T };
    }

    private static string ToParameterName(string propertyName)
    {
        return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }
}
