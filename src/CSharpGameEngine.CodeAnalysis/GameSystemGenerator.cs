using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructと<c>IScene</c>を実装するクラスから、閉じた集合として列挙される
/// Archetype群・<c>World</c>の構造変更API・メインループ・Sceneの<c>Instantiate</c>、そして条件を満たす
/// 場合はエントリポイントまで生成する(ADR-0033/ADR-0040、エントリポイントは
/// <see cref="GameSystemEntryPointEmitter"/>)。
///
/// <para>
/// 属性を一切使わない設計(Component/Systemはいずれもマーカー無しの構造的推論)のため、System検出には
/// <c>ForAttributeWithMetadataName</c>が使えず、<c>CreateSyntaxProvider</c>でbase listを持つ型宣言を
/// 拾って意味解析で<c>ISystem</c>実装を確認する(struct限定にすると「classで書いてしまった」という
/// SYSGEN001自体が到達不能になるため、あえてstruct限定にしない)。<c>IScene</c>も属性を持たないため
/// 同じ形で検出する(<see cref="SceneModelBridge"/>)。
/// </para>
///
/// <para>
/// Scene DSLがこのGeneratorへ統合されているのは必然的な設計判断である: Sceneのノード形状とSystemの
/// Update/Spawn形状は<b>同一のArchetype空間</b>に入る必要があり(両方が単一の<c>partial struct World</c>/
/// <c>enum Archetype</c>を作るため、生成は1回しか行えない)、2つのGeneratorが独立に同じレジストリを
/// 再導出する案はズレたときに「型が見つからない」という診断困難なビルド破壊になる。
/// </para>
///
/// <para>
/// <b>Scene DSLの診断(SCENE1xx)はここでは報告しない。</b>報告するのは<see cref="SceneDslAnalyzer"/>だけで、
/// このGeneratorは同じ解析結果を生成可否の判定にしか使わない(二重スクイグルの防止、ADR-0040)。
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
    public void Initialize(IncrementalGeneratorInitializationContext Context){
        IncrementalValuesProvider<GameSystemResult?>Candidates=Context.SyntaxProvider.CreateSyntaxProvider(
            static(Node,_)=>Node is TypeDeclarationSyntax { BaseList.Types.Count:>0 },
            static(Ctx,_)=>TryAnalyzeSystem(Ctx));

        IncrementalValuesProvider<GameSystemResult>Systems=Candidates
            .Where(static Result=>Result.HasValue)
            .Select(static(Result,_)=>Result!.Value);

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

        Context.RegisterSourceOutput(Systems,static(Spc,Result)=>GameSystemDiagnostics.ReportAll(Spc,Result));

        // Compilationそのものをキャッシュキーに含めると、キーストロークのたびに重い生成段が丸ごと
        // 再実行されてしまう。生成可否の判定に必要な事実だけを小さな比較可能値へ縮約する(ADR-0040)。
        IncrementalValueProvider<GenerationGate>Gate=Context.CompilationProvider.Select(
            static(Compilation,Ct)=>GenerationGate.FromCompilation(Compilation,Ct));

        IncrementalValueProvider<((ImmutableArray<GameSystemResult>Systems,ImmutableArray<GameSystemOrderResult>SystemOrders,ImmutableArray<SceneEmitModel>SceneDeclarations)Sources,GenerationGate Gate)>Combined=
            Systems.Collect()
                .Combine(SystemOrders.Collect())
                .Combine(SceneDeclarations.Collect())
                .Select(static(Data,_)=>(Data.Left.Left,Data.Left.Right,Data.Right))
                .Combine(Gate);

        Context.RegisterSourceOutput(Combined,static(Spc,Source)=>{
            if(!Source.Gate.HasRuntimeMarker){
                return;
            }

            ImmutableArray<GameSystemResult>SystemResults=Source.Sources.Systems;

            (GameSystemOrderContext OrderContext,ImmutableArray<Diagnostic>SystemOrderDiagnostics)=
                GameSystemOrderAnalyzer.BuildMap(Source.Sources.SystemOrders);

            foreach(Diagnostic SystemOrderDiagnostic in SystemOrderDiagnostics){
                Spc.ReportDiagnostic(SystemOrderDiagnostic);
            }

            // ADR-0040のScene DSL。Collect()の順序に依存しないよう必ず整列してから使う
            // (決定論性テストが両方の実行で同じ出力になることを要求している)。
            List<SceneEmitModel>ValidSceneDeclarations=[.. Source.Sources.SceneDeclarations.Where(static S=>S.CanEmit)];
            ValidSceneDeclarations.Sort(static(A,B)=>{
                var ByFile=string.CompareOrdinal(A.FilePath,B.FilePath);
                return ByFile!=0?ByFile:A.DeclarationPosition.CompareTo(B.DeclarationPosition);
            });

            // [StartupScene]が付いたSceneは「Order = Nのクエリ{Bootstrap}を持つ合成System」として、実際に
            // 検出されたSystemと同じモデル・同じ実行順序比較(GameSystemExecutionOrder)へ合流する
            // (理由はSceneStartupSystemEmitterのXMLコメント参照)。[StartupScene(Order = N)]の明示値は、
            // [SystemOrder]をSystem自身へ直接付けた場合と同じ優先度で扱うため、実際に検出されたSystemの
            // SystemOrderByFqnマップへ合成Systemの完全修飾名を追加登録したコピーを作る。
            List<GameSystemResult>ValidStartupSystems=[];
            Dictionary<string,int>MergedSystemOrderByFqn=new(StringComparer.Ordinal);

            foreach(KeyValuePair<string,int>ExistingOrder in OrderContext.SystemOrderByFqn){
                MergedSystemOrderByFqn[ExistingOrder.Key]=ExistingOrder.Value;
            }

            foreach(SceneEmitModel StartupScene in ValidSceneDeclarations.Where(static S=>S.IsStartupScene)){
                GameSystemResult SyntheticSystem=SceneStartupSystemEmitter.BuildSyntheticSystemResult(StartupScene);
                ValidStartupSystems.Add(SyntheticSystem);
                MergedSystemOrderByFqn[SyntheticSystem.ClassFullyQualifiedName]=StartupScene.StartupOrder;
            }

            GameSystemOrderContext MergedOrderContext=new(MergedSystemOrderByFqn,OrderContext.FileOrderByPath);
            ImmutableArray<GameSystemResult>AllSystemResults=[.. SystemResults,.. ValidStartupSystems];

            List<IReadOnlyList<string>>AdditionalComponentSets=[];

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

            foreach(Diagnostic Diagnostic in Registry.Diagnostics){
                Spc.ReportDiagnostic(Diagnostic);
            }

            GameSystemTypesEmitter.Emit(Spc,Registry);
            GameSystemWorldEmitter.Emit(Spc,Registry);
            GameSystemLoopEmitter.Emit(Spc,Registry);

            if(EntryPointPlan.Emit){
                GameSystemEntryPointEmitter.Emit(Spc,Registry);
            }

            foreach(SceneEmitModel SceneDeclaration in ValidSceneDeclarations){
                SceneInstantiateEmitter.Emit(Spc,SceneDeclaration,Registry);

                if(SceneDeclaration.IsStartupScene){
                    SceneStartupSystemEmitter.EmitHiddenSystem(Spc,SceneDeclaration);
                }
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
