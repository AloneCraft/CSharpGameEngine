using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructと<c>[SceneDefinition]</c>クラスから、閉じた集合として列挙される
/// Archetype群・<c>World</c>の構造変更API・メインループ・Sceneランタイムを生成する(ADR-0033)。
///
/// <para>
/// 属性を一切使わない設計(Component/Systemはいずれもマーカー無しの構造的推論)のため、System検出には
/// <c>ForAttributeWithMetadataName</c>が使えず、<c>CreateSyntaxProvider</c>でbase listを持つ型宣言を
/// 拾って意味解析で<c>ISystem</c>実装を確認する(struct限定にすると「classで書いてしまった」という
/// SYSGEN001自体が到達不能になるため、あえてstruct限定にしない)。
/// </para>
///
/// <para>
/// SceneDefinitionと統合されているのは必然的な設計判断である: Sceneのノード形状とSystemのUpdate/Spawn
/// 形状は<b>同一のArchetype空間</b>に入る必要があり(両方が単一の<c>partial struct World</c>/
/// <c>enum Archetype</c>を作るため、生成は1回しか行えない)、2つのGeneratorが独立に同じレジストリを
/// 再導出する案はズレたときに「型が見つからない」という診断困難なビルド破壊になる。
/// </para>
///
/// <para>
/// 出力は<c>ISystem</c>型がコンパイル内に実在する場合に限る。<c>CSharpGameEngine.CodeAnalysis</c>は
/// 1つのアナライザーパッケージに複数のGeneratorを同居させる規約(ADR-0014)のため、このECSを使う意図が
/// 無いプロジェクトでも参照だけで本Generatorが走ってしまい、無条件出力すると
/// <c>CSharpGameEngine.Entities</c>名前空間に空の<c>World</c>等が紛れ込んで実際にビルドを壊す
/// (ADR-0032実装時に確認済みの回帰)。
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GameSystemGenerator : IIncrementalGenerator
{
    /// <summary><c>GetTypeByMetadataName</c>用のメタデータ名(<c>global::</c>接頭辞は付けない)。</summary>
    private const string RuntimeMarkerMetadataName = "CSharpGameEngine.Entities.ISystem";

    private const string SceneDefinitionAttributeFullyQualifiedName = "CSharpGameEngine.Entities.SceneDefinitionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GameSystemResult?> candidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
            static (ctx, _) => TryAnalyzeSystem(ctx));

        IncrementalValuesProvider<GameSystemResult> systems = candidates
            .Where(static result => result.HasValue)
            .Select(static (result, _) => result!.Value);

        IncrementalValuesProvider<SceneDefinitionClassResult> scenes = context.SyntaxProvider.ForAttributeWithMetadataName(
            SceneDefinitionAttributeFullyQualifiedName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, ct) => SceneDefinitionAnalyzer.Analyze(ctx, ct));

        context.RegisterSourceOutput(systems, static (spc, result) => GameSystemDiagnostics.ReportAll(spc, result));

        context.RegisterSourceOutput(scenes, static (spc, result) =>
        {
            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }
        });

        // SceneNodeSymbolの生成はArchetypeレジストリに依存しない(SceneNodeSymbol/SceneNodeIdしか触らない)
        // ため、結合前の段で個別に出力できる。
        context.RegisterSourceOutput(scenes, static (spc, result) => SceneNodeSymbolEmitter.Emit(spc, result));

        IncrementalValueProvider<((ImmutableArray<GameSystemResult> Systems, ImmutableArray<SceneDefinitionClassResult> Scenes) Sources, Compilation Compilation)> combined =
            systems.Collect().Combine(scenes.Collect()).Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            if (source.Compilation.GetTypeByMetadataName(RuntimeMarkerMetadataName) is null)
            {
                return;
            }

            ImmutableArray<GameSystemResult> systemResults = source.Sources.Systems;
            ImmutableArray<SceneDefinitionClassResult> sceneResults = source.Sources.Scenes;

            List<IReadOnlyList<string>> sceneComponentSets = [];

            foreach (SceneDefinitionClassResult scene in sceneResults)
            {
                sceneComponentSets.AddRange(SceneRuntimeEmitter.CollectComponentSets(scene));
            }

            GameSystemRegistry registry = GameSystemIndexAssigner.Assign(systemResults, sceneComponentSets);

            foreach (Diagnostic diagnostic in registry.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }

            GameSystemTypesEmitter.Emit(spc, registry);
            GameSystemWorldEmitter.Emit(spc, registry);
            GameSystemSnapshotEmitter.Emit(spc, registry);
            GameSystemLoopEmitter.Emit(spc, registry);

            foreach (SceneDefinitionClassResult scene in sceneResults)
            {
                SceneRuntimeEmitter.Emit(spc, scene, registry);
            }
        });
    }

    private static GameSystemResult? TryAnalyzeSystem(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        bool implementsISystem = symbol.AllInterfaces.Any(static i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == GameSystemAnalyzer.SystemInterfaceFullyQualifiedName);

        return implementsISystem ? GameSystemAnalyzer.Analyze(symbol) : null;
    }
}
