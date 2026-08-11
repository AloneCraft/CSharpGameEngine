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
public sealed class GameSystemGenerator:IIncrementalGenerator {
    /// <summary><c>GetTypeByMetadataName</c>用のメタデータ名(<c>global::</c>接頭辞は付けない)。</summary>
    private const string RuntimeMarkerMetadataName="CSharpGameEngine.Entities.ISystem";

    private const string SceneDefinitionAttributeFullyQualifiedName="CSharpGameEngine.Entities.SceneDefinitionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext Context){
        IncrementalValuesProvider<GameSystemResult?>Candidates=Context.SyntaxProvider.CreateSyntaxProvider(
            static(Node,_)=>Node is TypeDeclarationSyntax { BaseList.Types.Count:>0 },
            static(Ctx,_)=>TryAnalyzeSystem(Ctx));

        IncrementalValuesProvider<GameSystemResult>Systems=Candidates
            .Where(static Result=>Result.HasValue)
            .Select(static(Result,_)=>Result!.Value);

        IncrementalValuesProvider<SceneDefinitionClassResult>Scenes=Context.SyntaxProvider.ForAttributeWithMetadataName(
            SceneDefinitionAttributeFullyQualifiedName,
            static(Node,_)=>Node is ClassDeclarationSyntax,
            static(Ctx,Ct)=>SceneDefinitionAnalyzer.Analyze(Ctx,Ct));

        // ADR-0040のScene DSL。`IScene`には属性が無いためForAttributeWithMetadataNameが使えず、
        // ISystemと同じく安価な構文プレディケートで絞ってから意味解析で確認する。
        // 流れるのはシンボルを持たないSceneEmitModelだけ(SceneModelBridgeのXMLコメント参照)。
        IncrementalValuesProvider<SceneEmitModel>SceneDeclarations=Context.SyntaxProvider.CreateSyntaxProvider(
                static(Node,_)=>SceneModelBridge.IsSceneCandidate(Node),
                static(Ctx,Ct)=>SceneModelBridge.TryAnalyze(Ctx,Ct))
            .Where(static Result=>Result.HasValue)
            .Select(static(Result,_)=>Result!.Value);

        // ADR-0036: ファイル横断・System横断の実行順序を宣言する唯一の属性。ISystem実装struct自身に
        // 直接付けるか、`file`スコープのマーカークラスへ付けてファイル既定値にするかのいずれか
        // (GameSystemOrderAnalyzer.Analyzeがどちらかを判別する)。
        IncrementalValuesProvider<GameSystemOrderResult>SystemOrders=Context.SyntaxProvider.ForAttributeWithMetadataName(
            GameSystemOrderAnalyzer.SystemOrderAttributeFullyQualifiedName,
            static(Node,_)=>Node is TypeDeclarationSyntax,
            static(Ctx,_)=>GameSystemOrderAnalyzer.Analyze(Ctx));

        // [StartupScene]: 手書きのGameStartup : ISystemの代わりに、Component初期値だけの宣言的Sceneから
        // Order = 0の{Bootstrap}合成Systemを作る(StartupSceneSystemEmitter参照)。SceneDefinitionと同様、
        // ノード形状はSystemのUpdate/Spawn形状と同一のArchetype空間へ合流する。
        IncrementalValuesProvider<StartupSceneClassResult>StartupScenes=Context.SyntaxProvider.ForAttributeWithMetadataName(
            StartupSceneAnalyzer.StartupSceneAttributeFullyQualifiedName,
            static(Node,_)=>Node is ClassDeclarationSyntax,
            static(Ctx,_)=>StartupSceneAnalyzer.Analyze(Ctx));

        Context.RegisterSourceOutput(Systems,static(Spc,Result)=>GameSystemDiagnostics.ReportAll(Spc,Result));

        Context.RegisterSourceOutput(Scenes,static(Spc,Result)=>{
            foreach(Diagnostic diagnostic in Result.Diagnostics){
                Spc.ReportDiagnostic(diagnostic);
            }
        });

        Context.RegisterSourceOutput(StartupScenes,static(Spc,Result)=>StartupSceneDiagnostics.ReportAll(Spc,Result));

        // SceneNodeSymbolの生成はArchetypeレジストリに依存しない(SceneNodeSymbol/SceneNodeIdしか触らない)
        // ため、結合前の段で個別に出力できる。
        Context.RegisterSourceOutput(Scenes,static(Spc,Result)=>SceneNodeSymbolEmitter.Emit(Spc,Result));

        // Compilationそのものをキャッシュキーに含めると、キーストロークのたびに重い生成段が丸ごと
        // 再実行されてしまう。生成可否の判定に必要な事実だけを小さな比較可能値へ縮約する(ADR-0040)。
        IncrementalValueProvider<GenerationGate>Gate=Context.CompilationProvider.Select(
            static(Compilation,Ct)=>GenerationGate.FromCompilation(Compilation,Ct));

        IncrementalValueProvider<((ImmutableArray<GameSystemResult>Systems,ImmutableArray<SceneDefinitionClassResult>Scenes,ImmutableArray<GameSystemOrderResult>SystemOrders,ImmutableArray<StartupSceneClassResult>StartupScenes,ImmutableArray<SceneEmitModel>SceneDeclarations)Sources,GenerationGate Gate)>Combined=
            Systems.Collect()
                .Combine(Scenes.Collect())
                .Combine(SystemOrders.Collect())
                .Combine(StartupScenes.Collect())
                .Combine(SceneDeclarations.Collect())
                .Select(static(Data,_)=>(Data.Left.Left.Left.Left,Data.Left.Left.Left.Right,Data.Left.Left.Right,Data.Left.Right,Data.Right))
                .Combine(Gate);

        Context.RegisterSourceOutput(Combined,static(Spc,Source)=>{
            if(!Source.Gate.HasRuntimeMarker){
                return;
            }

            ImmutableArray<GameSystemResult>SystemResults=Source.Sources.Systems;
            ImmutableArray<SceneDefinitionClassResult>SceneResults=Source.Sources.Scenes;
            ImmutableArray<StartupSceneClassResult>StartupSceneResults=Source.Sources.StartupScenes;

            (GameSystemOrderContext OrderContext,ImmutableArray<Diagnostic>SystemOrderDiagnostics)=
                GameSystemOrderAnalyzer.BuildMap(Source.Sources.SystemOrders);

            foreach(Diagnostic SystemOrderDiagnostic in SystemOrderDiagnostics){
                Spc.ReportDiagnostic(SystemOrderDiagnostic);
            }

            // [StartupScene]は「Order = 0のQuery {Bootstrap}を持つ合成System」として、実際に検出された
            // Systemと同じモデル・同じ実行順序比較(GameSystemExecutionOrder)へ合流する(理由は
            // StartupSceneSystemEmitterのXMLコメント参照)。[StartupScene(Order = N)]の明示値は、
            // [SystemOrder]をSystem自身へ直接付けた場合と同じ優先度で扱うため、実際に検出されたSystemの
            // SystemOrderByFqnマップへ合成Systemの完全修飾名を追加登録したコピーを作る。
            List<GameSystemResult>ValidStartupSystems=[];
            Dictionary<string,int>MergedSystemOrderByFqn=new(StringComparer.Ordinal);

            foreach(KeyValuePair<string,int>ExistingOrder in OrderContext.SystemOrderByFqn){
                MergedSystemOrderByFqn[ExistingOrder.Key]=ExistingOrder.Value;
            }

            foreach(StartupSceneClassResult StartupScene in StartupSceneResults){
                if(StartupScene.Diagnostics.Length>0){
                    continue;
                }

                GameSystemResult SyntheticSystem=StartupSceneSystemEmitter.BuildSyntheticSystemResult(StartupScene);
                ValidStartupSystems.Add(SyntheticSystem);
                MergedSystemOrderByFqn[SyntheticSystem.ClassFullyQualifiedName]=StartupScene.Order;
            }

            GameSystemOrderContext MergedOrderContext=new(MergedSystemOrderByFqn,OrderContext.FileOrderByPath);
            ImmutableArray<GameSystemResult>AllSystemResults=[.. SystemResults,.. ValidStartupSystems];

            List<IReadOnlyList<string>>AdditionalComponentSets=[];

            foreach(SceneDefinitionClassResult Scene in SceneResults){
                AdditionalComponentSets.AddRange(SceneRuntimeEmitter.CollectComponentSets(Scene));
            }

            foreach(StartupSceneClassResult StartupScene in StartupSceneResults){
                AdditionalComponentSets.AddRange(StartupSceneSystemEmitter.CollectComponentSets(StartupScene));
            }

            // ADR-0040のScene DSL。Collect()の順序に依存しないよう必ず整列してから収集する
            // (決定論性テストが両方の実行で同じ出力になることを要求している)。
            List<SceneEmitModel>ValidSceneDeclarations=[.. Source.Sources.SceneDeclarations.Where(static S=>S.CanEmit)];
            ValidSceneDeclarations.Sort(static(A,B)=>{
                var ByFile=string.CompareOrdinal(A.FilePath,B.FilePath);
                return ByFile!=0?ByFile:A.DeclarationPosition.CompareTo(B.DeclarationPosition);
            });

            foreach(SceneEmitModel SceneDeclaration in ValidSceneDeclarations){
                AdditionalComponentSets.AddRange(SceneComponentSetCollector.Collect(SceneDeclaration));
            }

            // エントリポイントを出すかどうかはレジストリ構築の前に決める。生成する場合にだけ
            // Bootstrapの形状を登録し、出さないコンパイルへ余計なArchetypeが増えないようにする。
            GameSystemEntryPointEmitter.Plan EntryPointPlan=
                GameSystemEntryPointEmitter.ShouldEmitPlan(Source.Gate,AllSystemResults,MergedOrderContext);

            if(EntryPointPlan.Diagnostic is { } EntryPointDiagnostic){
                Spc.ReportDiagnostic(EntryPointDiagnostic);
            }

            if(EntryPointPlan.Emit){
                AdditionalComponentSets.Add([GameSystemEntryPointEmitter.BootstrapComponentFullyQualifiedName]);
            }

            GameSystemRegistry Registry=GameSystemIndexAssigner.Assign(AllSystemResults,AdditionalComponentSets,MergedOrderContext);

            foreach(Diagnostic diagnostic in Registry.Diagnostics){
                Spc.ReportDiagnostic(diagnostic);
            }

            GameSystemTypesEmitter.Emit(Spc,Registry);
            GameSystemWorldEmitter.Emit(Spc,Registry);
            GameSystemSnapshotEmitter.Emit(Spc,Registry);
            GameSystemLoopEmitter.Emit(Spc,Registry);

            if(EntryPointPlan.Emit){
                GameSystemEntryPointEmitter.Emit(Spc,Registry);
            }

            foreach(SceneDefinitionClassResult scene in SceneResults){
                SceneRuntimeEmitter.Emit(Spc,scene,Registry);
            }

            foreach(StartupSceneClassResult startupScene in StartupSceneResults){
                StartupSceneSystemEmitter.EmitHiddenSystem(Spc,startupScene);
            }

            foreach(SceneEmitModel SceneDeclaration in ValidSceneDeclarations){
                SceneInstantiateEmitter.Emit(Spc,SceneDeclaration,Registry);
            }
        });
    }

    private static GameSystemResult?TryAnalyzeSystem(GeneratorSyntaxContext Ctx){
        if(Ctx.SemanticModel.GetDeclaredSymbol(Ctx.Node)is not INamedTypeSymbol Symbol){
            return null;
        }

        var ImplementsISystem=Symbol.AllInterfaces.Any(static I=>
            I.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==GameSystemAnalyzer.SystemInterfaceFullyQualifiedName);

        return ImplementsISystem?GameSystemAnalyzer.Analyze(Symbol):null;
    }
}
