using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructと<c>[SceneDefinition]</c>クラスから、閉じた集合として列挙される
/// Archetype群・<c>World</c>の構造変更API・メインループ・Sceneランタイム、そして条件を満たす場合は
/// エントリポイントまで生成する(ADR-0033、エントリポイントは<see cref="GameSystemEntryPointEmitter"/>)。
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

    public void Initialize(IncrementalGeneratorInitializationContext Context)
    {
        IncrementalValuesProvider<GameSystemResult?> Candidates = Context.SyntaxProvider.CreateSyntaxProvider(
            static (Node, _) => Node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
            static (Ctx, _) => TryAnalyzeSystem(Ctx));

        IncrementalValuesProvider<GameSystemResult> Systems = Candidates
            .Where(static Result => Result.HasValue)
            .Select(static (Result, _) => Result!.Value);

        IncrementalValuesProvider<SceneDefinitionClassResult> Scenes = Context.SyntaxProvider.ForAttributeWithMetadataName(
            SceneDefinitionAttributeFullyQualifiedName,
            static (Node, _) => Node is ClassDeclarationSyntax,
            static (Ctx, Ct) => SceneDefinitionAnalyzer.Analyze(Ctx, Ct));

        // ADR-0036: ファイル横断・System横断の実行順序を宣言する唯一の属性。ISystem実装struct自身に
        // 直接付けるか、`file`スコープのマーカークラスへ付けてファイル既定値にするかのいずれか
        // (GameSystemOrderAnalyzer.Analyzeがどちらかを判別する)。
        IncrementalValuesProvider<GameSystemOrderResult> SystemOrders = Context.SyntaxProvider.ForAttributeWithMetadataName(
            GameSystemOrderAnalyzer.SystemOrderAttributeFullyQualifiedName,
            static (Node, _) => Node is TypeDeclarationSyntax,
            static (Ctx, _) => GameSystemOrderAnalyzer.Analyze(Ctx));

        Context.RegisterSourceOutput(Systems, static (Spc, Result) => GameSystemDiagnostics.ReportAll(Spc, Result));

        Context.RegisterSourceOutput(Scenes, static (Spc, Result) =>
        {
            foreach (Diagnostic diagnostic in Result.Diagnostics)
            {
                Spc.ReportDiagnostic(diagnostic);
            }
        });

        // SceneNodeSymbolの生成はArchetypeレジストリに依存しない(SceneNodeSymbol/SceneNodeIdしか触らない)
        // ため、結合前の段で個別に出力できる。
        Context.RegisterSourceOutput(Scenes, static (Spc, Result) => SceneNodeSymbolEmitter.Emit(Spc, Result));

        IncrementalValueProvider<((ImmutableArray<GameSystemResult> Systems, ImmutableArray<SceneDefinitionClassResult> Scenes, ImmutableArray<GameSystemOrderResult> SystemOrders) Sources, Compilation Compilation)> Combined =
            Systems.Collect()
                .Combine(Scenes.Collect())
                .Combine(SystemOrders.Collect())
                .Select(static (Data, _) => (Data.Left.Left, Data.Left.Right, Data.Right))
                .Combine(Context.CompilationProvider);

        Context.RegisterSourceOutput(Combined, static (Spc, Source) =>
        {
            if (Source.Compilation.GetTypeByMetadataName(RuntimeMarkerMetadataName) is null)
            {
                return;
            }

            ImmutableArray<GameSystemResult> SystemResults = Source.Sources.Systems;
            ImmutableArray<SceneDefinitionClassResult> SceneResults = Source.Sources.Scenes;

            (GameSystemOrderContext OrderContext, ImmutableArray<Diagnostic> SystemOrderDiagnostics) =
                GameSystemOrderAnalyzer.BuildMap(Source.Sources.SystemOrders);

            foreach (Diagnostic SystemOrderDiagnostic in SystemOrderDiagnostics)
            {
                Spc.ReportDiagnostic(SystemOrderDiagnostic);
            }

            List<IReadOnlyList<string>> AdditionalComponentSets = [];

            foreach (SceneDefinitionClassResult Scene in SceneResults)
            {
                AdditionalComponentSets.AddRange(SceneRuntimeEmitter.CollectComponentSets(Scene));
            }

            // エントリポイントを出すかどうかはレジストリ構築の前に決める。生成する場合にだけ
            // Bootstrapの形状を登録し、出さないコンパイルへ余計なArchetypeが増えないようにする。
            GameSystemEntryPointEmitter.Plan EntryPointPlan =
                GameSystemEntryPointEmitter.ShouldEmitPlan(Source.Compilation, SystemResults, OrderContext, Spc.CancellationToken);

            if (EntryPointPlan.Diagnostic is { } EntryPointDiagnostic)
            {
                Spc.ReportDiagnostic(EntryPointDiagnostic);
            }

            if (EntryPointPlan.Emit)
            {
                AdditionalComponentSets.Add([GameSystemEntryPointEmitter.BootstrapComponentFullyQualifiedName]);
            }

            GameSystemRegistry Registry = GameSystemIndexAssigner.Assign(SystemResults, AdditionalComponentSets, OrderContext);

            foreach (Diagnostic diagnostic in Registry.Diagnostics)
            {
                Spc.ReportDiagnostic(diagnostic);
            }

            GameSystemTypesEmitter.Emit(Spc, Registry);
            GameSystemWorldEmitter.Emit(Spc, Registry);
            GameSystemSnapshotEmitter.Emit(Spc, Registry);
            GameSystemLoopEmitter.Emit(Spc, Registry);

            if (EntryPointPlan.Emit)
            {
                GameSystemEntryPointEmitter.Emit(Spc, Registry);
            }

            foreach (SceneDefinitionClassResult scene in SceneResults)
            {
                SceneRuntimeEmitter.Emit(Spc, scene, Registry);
            }
        });
    }

    private static GameSystemResult? TryAnalyzeSystem(GeneratorSyntaxContext Ctx)
    {
        if (Ctx.SemanticModel.GetDeclaredSymbol(Ctx.Node) is not INamedTypeSymbol Symbol)
        {
            return null;
        }

        bool ImplementsISystem = Symbol.AllInterfaces.Any(static I =>
            I.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == GameSystemAnalyzer.SystemInterfaceFullyQualifiedName);

        return ImplementsISystem ? GameSystemAnalyzer.Analyze(Symbol) : null;
    }
}
