using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[GenerateEntity]</c>が付いた構造体のシンボル解析: partial/ジェネリック/nesting検証、
/// 属性引数からのStorageKind定数解決、コンポーネントプロパティの列挙・検証、
/// 完全修飾名によるOrdinal正規化、生成メンバー名・生成型名との衝突検出を行う。
/// </summary>
internal static class GenerateEntityAnalyzer
{
    internal static GenerateEntityAnalysisResult Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        INamedTypeSymbol structSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (ctx.TargetNode is StructDeclarationSyntax structDeclaration && !structDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.MustBePartialStruct, structDeclaration.Identifier.GetLocation(), structSymbol.Name));
        }

        if (structSymbol.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.MustNotBeGeneric, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        if (ctx.TargetNode.SyntaxTree.Options is CSharpParseOptions parseOptions && parseOptions.LanguageVersion < LanguageVersion.CSharp13)
        {
            diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.RequiresPartialPropertiesLanguageVersion, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        ImmutableArray<GenerateEntityContainingTypeModel> containingTypeChain = BuildContainingTypeChain(structSymbol, diagnostics);
        GenerateEntityStorageKind storageKind = ResolveStorageKind(ctx, structSymbol, diagnostics);
        List<(string PropertyName, string ComponentTypeFullyQualifiedName, int DeclarationIndex)> acceptedComponents = AnalyzeComponentProperties(structSymbol, diagnostics);

        if (acceptedComponents.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.MustHaveAtLeastOneComponent, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        CheckGeneratedMemberNameCollisions(structSymbol, acceptedComponents, diagnostics);
        CheckGeneratedTypeNameCollisions(structSymbol, diagnostics);

        List<(string PropertyName, string ComponentTypeFullyQualifiedName, int DeclarationIndex)> normalizedOrder =
            [.. acceptedComponents.OrderBy(c => c.ComponentTypeFullyQualifiedName, StringComparer.Ordinal)];

        Dictionary<string, int> normalizedIndexByPropertyName = new(StringComparer.Ordinal);
        for (int i = 0; i < normalizedOrder.Count; i++)
        {
            normalizedIndexByPropertyName[normalizedOrder[i].PropertyName] = i;
        }

        ImmutableArray<GenerateEntityComponentModel>.Builder declarationOrderedBuilder = ImmutableArray.CreateBuilder<GenerateEntityComponentModel>(acceptedComponents.Count);

        foreach ((string propertyName, string componentTypeFullyQualifiedName, int declarationIndex) in acceptedComponents)
        {
            declarationOrderedBuilder.Add(new GenerateEntityComponentModel(
                propertyName,
                componentTypeFullyQualifiedName,
                normalizedIndexByPropertyName[propertyName],
                declarationIndex));
        }

        ImmutableArray<GenerateEntityComponentModel>.Builder normalizedOrderedBuilder = ImmutableArray.CreateBuilder<GenerateEntityComponentModel>(normalizedOrder.Count);

        foreach ((string propertyName, string componentTypeFullyQualifiedName, int declarationIndex) in normalizedOrder)
        {
            normalizedOrderedBuilder.Add(new GenerateEntityComponentModel(
                propertyName,
                componentTypeFullyQualifiedName,
                normalizedIndexByPropertyName[propertyName],
                declarationIndex));
        }

        return new GenerateEntityAnalysisResult(
            structSymbol.ContainingNamespace.IsGlobalNamespace ? null : structSymbol.ContainingNamespace.ToDisplayString(),
            containingTypeChain,
            structSymbol.Name,
            storageKind,
            declarationOrderedBuilder.ToImmutable(),
            normalizedOrderedBuilder.ToImmutable(),
            diagnostics.ToImmutable());
    }

    private static ImmutableArray<GenerateEntityContainingTypeModel> BuildContainingTypeChain(INamedTypeSymbol structSymbol, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        List<GenerateEntityContainingTypeModel> chain = [];
        bool allContainingTypesArePartial = true;
        INamedTypeSymbol? containingType = structSymbol.ContainingType;

        while (containingType is not null)
        {
            string keyword = containingType.TypeKind switch
            {
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                _ => "class",
            };

            chain.Insert(0, new GenerateEntityContainingTypeModel(containingType.Name, keyword));

            bool containingIsPartial = containingType.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(t => t.Modifiers.Any(SyntaxKind.PartialKeyword));

            if (!containingIsPartial)
            {
                allContainingTypesArePartial = false;
            }

            containingType = containingType.ContainingType;
        }

        if (chain.Count > 0 && !allContainingTypesArePartial)
        {
            diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.ContainingTypeMustBePartial, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        }

        return [.. chain];
    }

    private static GenerateEntityStorageKind ResolveStorageKind(GeneratorAttributeSyntaxContext ctx, INamedTypeSymbol structSymbol, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        AttributeData? attributeData = ctx.Attributes.Length > 0 ? ctx.Attributes[0] : null;
        TypedConstant? kindArgument = attributeData is not null && attributeData.ConstructorArguments.Length > 0
            ? attributeData.ConstructorArguments[0]
            : null;

        // EntityStorageKind(CSharpGameEngine.Generation.Abstractions)は[Flags]で
        // Aos=1, EcsSoa=2 として宣言されている。このプロジェクトは
        // (ADR-0014のRuntime非依存方針に従い)そのアセンブリを参照しないため、
        // 序数を直接ここへミラーする。1(AoSのみ)/2(SoAのみ)/3(両方)だけを受理する。
        if (kindArgument is { Kind: not TypedConstantKind.Error, Value: int kindValue } && kindValue is 1 or 2 or 3)
        {
            return (GenerateEntityStorageKind)kindValue;
        }

        diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.StorageKindMustBeResolvableConstant, structSymbol.Locations.FirstOrDefault(), structSymbol.Name));
        return GenerateEntityStorageKind.None;
    }

    private static List<(string PropertyName, string ComponentTypeFullyQualifiedName, int DeclarationIndex)> AnalyzeComponentProperties(
        INamedTypeSymbol structSymbol,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        List<(string PropertyName, string ComponentTypeFullyQualifiedName, int DeclarationIndex)> accepted = [];
        Dictionary<string, IPropertySymbol> seenComponentTypes = new(StringComparer.Ordinal);

        foreach (IPropertySymbol property in structSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            Location location = property.Locations.FirstOrDefault() ?? Location.None;

            if (property.IsIndexer)
            {
                if (property.IsPartialDefinition)
                {
                    diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.ComponentPropertyMustNotBeIndexer, location, structSymbol.Name, property.Name));
                }

                continue;
            }

            // partialであること自体が「このプロパティをComponentとして生成してほしい」という
            // 明示的な意思表示になる。partialではないが、get/setを両方持つ非staticプロパティは
            // 「Componentのつもりでpartialを付け忘れた」可能性が高い形なので、静かに無視せず診断する。
            // それ以外の通常プロパティ・メソッド・static・constはそもそも候補にせず、診断もしない。
            bool looksLikeComponentCandidate = property.IsPartialDefinition
                || (!property.IsStatic && property.GetMethod is not null && property.SetMethod is not null);

            if (!looksLikeComponentCandidate)
            {
                continue;
            }

            if (!property.IsPartialDefinition)
            {
                diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.ComponentPropertyMustBePartial, location, structSymbol.Name, property.Name));
                continue;
            }

            if (property.IsStatic)
            {
                diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.ComponentPropertyMustNotBeStatic, location, structSymbol.Name, property.Name));
                continue;
            }

            if (property.GetMethod is null || property.SetMethod is null)
            {
                diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.ComponentPropertyMustHaveGetAndSet, location, structSymbol.Name, property.Name));
                continue;
            }

            if (property.SetMethod.IsInitOnly)
            {
                diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.ComponentPropertyMustNotBeInitOnly, location, structSymbol.Name, property.Name));
                continue;
            }

            if (!IsUnmanagedStruct(property.Type))
            {
                diagnostics.Add(Diagnostic.Create(
                    GenerateEntityDiagnostics.ComponentTypeMustBeUnmanagedStruct,
                    location,
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            string componentTypeFullyQualifiedName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (seenComponentTypes.ContainsKey(componentTypeFullyQualifiedName))
            {
                diagnostics.Add(Diagnostic.Create(GenerateEntityDiagnostics.DuplicateComponentType, location, structSymbol.Name, property.Type.Name));
                continue;
            }

            seenComponentTypes.Add(componentTypeFullyQualifiedName, property);
            accepted.Add((property.Name, componentTypeFullyQualifiedName, accepted.Count));
        }

        return accepted;
    }

    private static void CheckGeneratedMemberNameCollisions(
        INamedTypeSymbol structSymbol,
        List<(string PropertyName, string ComponentTypeFullyQualifiedName, int DeclarationIndex)> acceptedComponents,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // "Entity"/"IsAlive"/"Destroy"はFacade({StructName})自身に生成される。"Count"/"Entities"は
        // Aos/SoAどちらのChunkViewにも生成されるため、同名のComponentプロパティは
        // (ChunkView側でのみ衝突するにもかかわらず)ここで一緒に検出する。
        HashSet<string> reservedMemberNames = new(StringComparer.Ordinal) { "Entity", "IsAlive", "Destroy", "Count", "Entities" };

        foreach ((string propertyName, _, _) in acceptedComponents)
        {
            reservedMemberNames.Add(propertyName + "Ref");
        }

        foreach (ISymbol member in structSymbol.GetMembers())
        {
            if (!reservedMemberNames.Contains(member.Name))
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                GenerateEntityDiagnostics.GeneratedMemberNameCollision,
                member.Locations.FirstOrDefault() ?? Location.None,
                structSymbol.Name,
                member.Name));
        }
    }

    private static void CheckGeneratedTypeNameCollisions(INamedTypeSymbol structSymbol, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // 両指定モードでは型名が{Type}Aos*/{Type}Soa*へ切り替わるため、実際に選択された
        // StorageKindに関わらず、生成しうる名前をすべて予約しておく。
        HashSet<string> reservedTypeNames = new(StringComparer.Ordinal)
        {
            structSymbol.Name + "CreateInfo",
            structSymbol.Name + "Store",
            structSymbol.Name + "AosStore",
            structSymbol.Name + "SoaStore",
            structSymbol.Name + "GeneratedMetadata",
            structSymbol.Name + "ChunkView",
            structSymbol.Name + "AosChunkView",
            structSymbol.Name + "SoaChunkView",
            structSymbol.Name + "AosGeneratedData",
            structSymbol.Name + "SoaGeneratedArchetype",
        };

        INamespaceOrTypeSymbol container = structSymbol.ContainingType is not null
            ? structSymbol.ContainingType
            : structSymbol.ContainingNamespace;

        foreach (ISymbol member in container.GetMembers())
        {
            if (member is not INamedTypeSymbol namedType || SymbolEqualityComparer.Default.Equals(namedType, structSymbol))
            {
                continue;
            }

            if (!reservedTypeNames.Contains(namedType.Name))
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                GenerateEntityDiagnostics.GeneratedTypeNameCollision,
                structSymbol.Locations.FirstOrDefault(),
                structSymbol.Name,
                namedType.Name));
        }
    }

    private static bool IsUnmanagedStruct(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T })
        {
            return false;
        }

        return type.IsUnmanagedType;
    }
}
