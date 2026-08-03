using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[CSharpGameEngine.Ecs.EcsArchetype]</c>が付いたpartial structから、正規化済み
/// コンポーネント構成に基づくpartialプロパティ実装・<c>IEcsArchetype&lt;T&gt;</c>静的メンバー・
/// 名前付き引数版ファクトリを生成する。ランタイム型(<c>CSharpGameEngine.Ecs.World</c>等)へは
/// 文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisからCSharpGameEngine.Ecsへの
/// アセンブリ参照は発生させない(ADR-0001: ランタイムはRoslynに依存してはならない、の逆方向を保つ)。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EcsArchetypeGenerator : IIncrementalGenerator
{
    private const string EcsArchetypeAttributeFullyQualifiedName = "CSharpGameEngine.Ecs.EcsArchetypeAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<EcsArchetypeAnalysisResult> results = context.SyntaxProvider.ForAttributeWithMetadataName(
            EcsArchetypeAttributeFullyQualifiedName,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax,
            static (ctx, _) => EcsArchetypeAnalyzer.Analyze(ctx));

        context.RegisterSourceOutput(results, static (spc, result) => EcsArchetypeEmitter.Emit(spc, result));
    }
}
