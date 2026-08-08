using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructから、閉じた集合として列挙されるArchetype群・<c>World</c>の構造変更API・
/// メインループを生成する(ADR-0032)。既存の<c>GameSystemGenerator</c>と異なり属性を一切使わない設計
/// (Component/Systemはいずれもマーカー無しの構造的推論)のため<c>ForAttributeWithMetadataName</c>は
/// 使えず、<c>CreateSyntaxProvider</c>でbase listを持つ型宣言(class/struct/record/interfaceいずれも
/// <c>TypeDeclarationSyntax</c>)を拾い、意味解析で<c>ISystem</c>実装を確認する(struct限定にすると
/// 「classで書いてしまった」というSARCH001自体が到達不能になるため、あえてstruct限定にしない)。
/// ランタイム型へは文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisから
/// CSharpGameEngine.StaticArchetypeへのアセンブリ参照は発生させない(ADR-0001、既存ジェネレータと同じ方針)。
///
/// <para>
/// <c>CSharpGameEngine.CodeAnalysis</c>は1つのアナライザーパッケージに複数のGeneratorを同居させる
/// 既存規約(ADR-0014)のため、<c>UserGameProject</c>のように本Generatorの設計を使う意図が一切ない
/// プロジェクトでも、パッケージ参照だけで(GameSystemGenerator目的で)本Generatorが実行されてしまう。
/// <c>Archetype</c> enum/<c>World</c>スタブを無条件に出力すると、そのプロジェクトのグローバル
/// 名前空間に空の<c>World</c>型が紛れ込み、<c>using CSharpGameEngine.Entities;</c>経由で使われている
/// 本物の<c>CSharpGameEngine.Entities.World</c>を陰から覆い隠してしまう(グローバル名前空間の型は
/// using有効化された名前空間の型よりC#の名前解決で優先されるため、実際に発生を確認した回帰)。
/// そのため、<c>ISystem</c>型(<c>Runtime.cs</c>由来)がコンパイル内に実在する場合に限り出力する
/// ことで、<c>Runtime.cs</c>を意図的に取り込んだプロジェクトだけに出力を限定する。
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StaticArchetypeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<StaticArchetypeSystemResult?> candidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
            static (ctx, _) => TryAnalyze(ctx));

        IncrementalValuesProvider<StaticArchetypeSystemResult> systems = candidates
            .Where(static result => result.HasValue)
            .Select(static (result, _) => result!.Value);

        context.RegisterSourceOutput(systems, static (spc, result) => StaticArchetypeDiagnostics.ReportAll(spc, result));

        IncrementalValueProvider<(ImmutableArray<StaticArchetypeSystemResult> Systems, Compilation Compilation)> combined =
            systems.Collect().Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            if (source.Compilation.GetTypeByMetadataName(RuntimeMarkerMetadataName) is null)
            {
                return;
            }

            StaticArchetypeRegistry registry = StaticArchetypeIndexAssigner.Assign(source.Systems);
            StaticArchetypeTypesEmitter.Emit(spc, registry);
            StaticArchetypeWorldEmitter.Emit(spc, registry);
            StaticArchetypeLoopEmitter.Emit(spc, registry);
        });
    }

    /// <summary>グローバル名前空間の<c>ISystem</c>のメタデータ名(<c>GetTypeByMetadataName</c>用、<c>global::</c>接頭辞は付けない)。</summary>
    private const string RuntimeMarkerMetadataName = "ISystem";

    private static StaticArchetypeSystemResult? TryAnalyze(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        bool implementsISystem = symbol.AllInterfaces.Any(static i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == StaticArchetypeAnalyzer.SystemInterfaceFullyQualifiedName);

        return implementsISystem ? StaticArchetypeAnalyzer.Analyze(symbol) : null;
    }
}
