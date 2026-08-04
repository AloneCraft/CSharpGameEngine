using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[CSharpGameEngine.Generation.GenerateEntity]</c>が付いたpartial structから、
/// 属性引数の<c>EntityStorageKind</c>定数値に応じてAoSまたはECS SoAのどちらか一方だけの
/// 物理ストレージ実装を生成する。ランタイム型(<c>CSharpGameEngine.Ecs.World</c>等)へは
/// 文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisからCSharpGameEngine.Ecsや
/// CSharpGameEngine.Generation.Abstractionsへのアセンブリ参照は発生させない
/// (ADR-0001: ランタイムはRoslynに依存してはならない、の逆方向を保つ)。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GenerateEntityGenerator : IIncrementalGenerator
{
    private const string GenerateEntityAttributeFullyQualifiedName = "CSharpGameEngine.Generation.GenerateEntityAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GenerateEntityAnalysisResult> results = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateEntityAttributeFullyQualifiedName,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax,
            static (ctx, ct) => GenerateEntityAnalyzer.Analyze(ctx, ct));

        context.RegisterSourceOutput(results, static (spc, result) => Emit(spc, result));
    }

    private static void Emit(SourceProductionContext context, GenerateEntityAnalysisResult result)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (result.Diagnostics.Length > 0)
        {
            return;
        }

        // Facade/CreateInfo/GeneratedMetadataは両バックエンドで同一のソースになるため、
        // 両指定モードでも1回だけ出力する(重複定義を避ける)。
        GenerateEntityCommonEmitter.Emit(context, result);

        // StorageKindによる分岐はGenerator自身の内部処理に限定される。選択されなかった
        // バックエンドのAddSource呼び出しは一切行われないため、出力ソースには
        // どちらのバックエンドが選択されたかを示すRuntime分岐は一切現れない。
        if (result.HasAos)
        {
            GenerateEntityAosEmitter.Emit(context, result);
        }

        if (result.HasSoa)
        {
            GenerateEntitySoaEmitter.Emit(context, result);
        }
    }
}
