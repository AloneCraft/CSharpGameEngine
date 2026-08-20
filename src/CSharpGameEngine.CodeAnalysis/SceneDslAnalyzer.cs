using System.Collections.Concurrent;
using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Scene DSLの診断を報告する<b>唯一の主体</b>(ADR-0040)。本リポジトリで初めての
/// <see cref="DiagnosticAnalyzer"/>である。
///
/// <para>
/// 既存の診断(SYSGEN/SCENE001-008/STARTUP)はすべてGeneratorの<c>SourceProductionContext</c>から
/// 流れるが、Scene DSLだけはアナライザーにした。Generatorの診断はIDEのバックグラウンド解析サイクルに
/// 縛られてスクイグルが遅れる一方、宣言DSLは<b>入力中の即時フィードバックが要件</b>だからである。
/// アナライザーはコマンドラインビルドでも走るため、ビルド時の検証も同時に担保される。
/// </para>
///
/// <para>
/// <b><see cref="GameSystemGenerator"/>はSCENE1xxを報告しない。</b>同じ
/// <see cref="SceneDeclarationAnalyzer"/>を走らせるが、結果は生成可否の判定にしか使わない。
/// 両方が報告すると同じ箇所に二重のスクイグルが出る。
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneDslAnalyzer:DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>[.. SceneDslDiagnostics.All];

    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();

        // 生成コードは解析しない。SGが出す{Class}.Scene.g.csはScene DSLの成果物であって入力ではない。
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        Context.RegisterCompilationStartAction(static CompilationStart=>{
            // CSharpGameEngine.CodeAnalysisは1つのアナライザーパッケージに複数の機能を同居させる規約
            // (ADR-0014)のため、このECSを使う意図が無いプロジェクトでも参照だけでこのアナライザーが走る。
            // ランタイム型の実在をゲートにして誤爆を防ぐ(GameSystemGeneratorの同名のゲートと同じ理由)。
            if(CompilationStart.Compilation.GetTypeByMetadataName(SceneDslWellKnownNames.SceneInterfaceMetadataName) is null){
                return;
            }

            SceneDslAnalysisState State=new();

            CompilationStart.RegisterSymbolAction(
                Symbol=>AnalyzeNamedType(Symbol,State),
                SymbolKind.NamedType);

            CompilationStart.RegisterOperationAction(
                AnalyzeObjectCreation,
                OperationKind.ObjectCreation);

            CompilationStart.RegisterOperationAction(
                AnalyzePropertyReference,
                OperationKind.PropertyReference);

            CompilationStart.RegisterCompilationEndAction(State.ReportStartupSceneOrderConflicts);
        });
    }

    /// <summary>
    /// <c>IScene</c>実装クラスを<see cref="SceneDeclarationAnalyzer"/>へ渡し、得られた診断をそのまま流す。
    /// <c>[StartupScene]</c>が<c>IScene</c>実装でない型に付いている場合(SCENE117)もここで拾う —
    /// その属性は<c>Instantiate</c>を持たない型に対しては何の意味も持たず、静かに無視されるため。
    /// </summary>
    private static void AnalyzeNamedType(SymbolAnalysisContext Context,SceneDslAnalysisState State){
        var Type=(INamedTypeSymbol)Context.Symbol;
        var ImplementsScene=SceneTypePredicates.ImplementsSceneInterface(Type);
        (var IsStartupScene,var Order)=SceneDeclarationAnalyzer.ReadStartupScene(Type);

        if(IsStartupScene&&!ImplementsScene){
            Context.ReportDiagnostic(Diagnostic.Create(
                SceneDslDiagnostics.StartupSceneMustImplementIScene,
                Type.Locations.FirstOrDefault(),
                Type.Name));
            return;
        }

        if(!ImplementsScene){
            return;
        }

        var Model=SceneDeclarationAnalyzer.Analyze(Type,Context.Compilation,Context.CancellationToken);

        foreach(var Diagnostic in Model.Diagnostics){
            Context.ReportDiagnostic(Diagnostic);
        }

        if(IsStartupScene){
            State.RecordStartupScene(Order,Model.ClassFullyQualifiedName,Type.Locations.FirstOrDefault());
        }
    }

    /// <summary>
    /// SCENE114: <c>new Node { … }</c>をScene宣言の外で書いていないか。
    ///
    /// <para>
    /// Scene DSLがRuntimeで実行されないことを言語機能で保証する手段は無い(<c>Root</c>のプロパティ本体は
    /// ILへコンパイルされる)。防御は3重で、これはその1つ目である: <c>Node</c>のコンストラクターが常に
    /// 例外を投げること(実行したら落ちる)、この診断(Scene宣言の外では書けない)、そしてSGが生成partialへ
    /// privateコンストラクターを出すこと(<c>new MainScene()</c>ができない)。
    /// </para>
    /// </summary>
    private static void AnalyzeObjectCreation(OperationAnalysisContext Context){
        var Operation=(IObjectCreationOperation)Context.Operation;

        if(Operation.Type is not { } Type
            ||SceneTypePredicates.ToFullyQualifiedName(Type)!=SceneDslWellKnownNames.NodeFullyQualifiedName){
            return;
        }

        if(IsInsideSceneRootProperty(Context.ContainingSymbol)){
            return;
        }

        Context.ReportDiagnostic(Diagnostic.Create(
            SceneDslDiagnostics.SceneDslTypeUsedOutsideDeclaration,
            Operation.Syntax.GetLocation(),
            SceneDslWellKnownNames.NodeFullyQualifiedName));
    }

    /// <summary>SCENE114: <c>scene.Root</c>を読む(= Scene DSLを実行する)コードを禁止する。</summary>
    private static void AnalyzePropertyReference(OperationAnalysisContext Context){
        var Operation=(IPropertyReferenceOperation)Context.Operation;
        var Property=Operation.Property;

        if(Property.Type is not { } PropertyType
            ||SceneTypePredicates.ToFullyQualifiedName(PropertyType)!=SceneDslWellKnownNames.NodeFullyQualifiedName
            ||!IsRootPropertyName(Property.Name)
            ||Property.ContainingType is not { } Owner
            ||!SceneTypePredicates.ImplementsSceneInterface(Owner)){
            return;
        }

        Context.ReportDiagnostic(Diagnostic.Create(
            SceneDslDiagnostics.SceneDslTypeUsedOutsideDeclaration,
            Operation.Syntax.GetLocation(),
            $"{Owner.Name}.{SceneDslWellKnownNames.RootPropertyName}"));
    }

    /// <summary>
    /// この操作が<c>IScene.Root</c>のgetter本体の中にあるか。プロパティのgetterに対応する
    /// <see cref="IMethodSymbol"/>の<see cref="IMethodSymbol.AssociatedSymbol"/>がプロパティ本体を指す。
    /// </summary>
    private static bool IsInsideSceneRootProperty(ISymbol?ContainingSymbol){
        return ContainingSymbol is IMethodSymbol { AssociatedSymbol:IPropertySymbol Property }
            &&IsRootPropertyName(Property.Name)
            &&Property.ContainingType is { } Owner
            &&SceneTypePredicates.ImplementsSceneInterface(Owner);
    }

    /// <summary>明示的インターフェース実装では名前が<c>CSharpGameEngine.Entities.IScene.Root</c>になる。</summary>
    private static bool IsRootPropertyName(string Name){
        return Name==SceneDslWellKnownNames.RootPropertyName
            ||Name.EndsWith("."+SceneDslWellKnownNames.RootPropertyName,StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>[StartupScene(Order = N)]</c>の重複(SCENE118)はコンパイル全体を見ないと判定できないため、
    /// 型ごとの解析で収集してCompilationEndで報告する。
    ///
    /// <para>
    /// <see cref="AnalysisContext.EnableConcurrentExecution"/>を有効にしているのでSymbolActionは
    /// 並列に走る。状態は必ずスレッドセーフな構造で持つこと。
    /// </para>
    /// </summary>
    private sealed class SceneDslAnalysisState {
        private readonly ConcurrentDictionary<int,(string ClassFullyQualifiedName,Location?Location)>StartupScenesByOrder=new();
        private readonly ConcurrentBag<(int Order,string ClassFullyQualifiedName,string ExistingClassFullyQualifiedName,Location?Location)>Conflicts=[];

        internal void RecordStartupScene(int Order,string ClassFullyQualifiedName,Location?Location){
            if(this.StartupScenesByOrder.TryAdd(Order,(ClassFullyQualifiedName,Location))){
                return;
            }

            if(this.StartupScenesByOrder.TryGetValue(Order,out var Existing)
                &&Existing.ClassFullyQualifiedName!=ClassFullyQualifiedName){
                this.Conflicts.Add((Order,ClassFullyQualifiedName,Existing.ClassFullyQualifiedName,Location));
            }
        }

        internal void ReportStartupSceneOrderConflicts(CompilationAnalysisContext Context){
            foreach((var Order,var ClassFullyQualifiedName,var ExistingClassFullyQualifiedName,var Location)in this.Conflicts){
                Context.ReportDiagnostic(Diagnostic.Create(
                    SceneDslDiagnostics.DuplicateStartupSceneOrder,
                    Location,
                    Order,
                    ExistingClassFullyQualifiedName,
                    ClassFullyQualifiedName));
            }
        }
    }
}
