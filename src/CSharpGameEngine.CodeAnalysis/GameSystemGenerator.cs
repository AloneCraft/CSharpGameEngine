using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[CSharpGameEngine.Entities.GameSystem]</c>が付いた静的クラスから、メソッドごとのExecutorと、
/// コンパイル全体をまたぐ単一の<c>GeneratedGameLoop</c>を生成する。ランタイム型へは
/// <see cref="GameSystemAnalyzer"/>内で文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisから
/// CSharpGameEngine.Entitiesへのアセンブリ参照は発生させない(ADR-0001、既存2ジェネレータと同じ方針)。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GameSystemGenerator : IIncrementalGenerator
{
    private const string GameSystemAttributeFullyQualifiedName = "CSharpGameEngine.Entities.GameSystemAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GameSystemClassResult> classes = context.SyntaxProvider.ForAttributeWithMetadataName(
            GameSystemAttributeFullyQualifiedName,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
            static (ctx, ct) => GameSystemAnalyzer.Analyze(ctx, ct));

        context.RegisterSourceOutput(classes, static (spc, result) => GameSystemExecutorEmitter.Emit(spc, result));

        context.RegisterSourceOutput(classes.Collect(), static (spc, results) => GameSystemLoopEmitter.Emit(spc, results));
    }
}
