using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[CSharpGameEngine.Entities.SceneDefinition]</c>が付いた静的partialクラスから、各<c>SceneNodeSymbol</c>
/// プロパティの実装本体(<see cref="SceneNodeSymbolEmitter"/>)と、<c>CreateRuntime</c>によるRuntime構築
/// コード(<see cref="SceneRuntimeEmitter"/>)を生成する。ランタイム型へは<see cref="SceneDefinitionAnalyzer"/>
/// 内で文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisからCSharpGameEngine.Entitiesへのアセンブリ
/// 参照は発生させない(ADR-0001、既存2ジェネレータと同じ方針)。生成物はクラスごとに独立しており、
/// GameSystemGeneratorのGeneratedGameLoopのようなsolution横断の結合出力は不要なため、.Collect()は使わない。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SceneDefinitionGenerator : IIncrementalGenerator
{
    private const string SceneDefinitionAttributeFullyQualifiedName = "CSharpGameEngine.Entities.SceneDefinitionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<SceneDefinitionClassResult> classes = context.SyntaxProvider.ForAttributeWithMetadataName(
            SceneDefinitionAttributeFullyQualifiedName,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
            static (ctx, ct) => SceneDefinitionAnalyzer.Analyze(ctx, ct));

        context.RegisterSourceOutput(classes, static (spc, result) =>
        {
            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }
        });

        context.RegisterSourceOutput(classes, static (spc, result) => SceneNodeSymbolEmitter.Emit(spc, result));

        context.RegisterSourceOutput(classes, static (spc, result) => SceneRuntimeEmitter.Emit(spc, result));
    }
}
