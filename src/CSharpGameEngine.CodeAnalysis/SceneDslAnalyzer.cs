using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// The C# side of the scene boundary (ADR-0070). Scenes are JSON documents under <c>Scenes</c>, reported by
/// <see cref="SceneJsonDslAnalyzer"/>; in C# this analyzer reports only what must not remain there:
/// a C# scene class or <c>[StartupScene]</c> (SCENE206, with the converter to run), and code that
/// builds or reads scene <c>Node</c> trees (SCENE114, see below).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneDslAnalyzer:DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>
        ImmutableArray.Create(SceneDslDiagnostics.SceneDslTypeUsedOutsideDeclaration,SceneJsonDiagnostics.CSharpSceneNotBuilt);

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

            CompilationStart.RegisterSymbolAction(
                AnalyzeNamedType,
                SymbolKind.NamedType);

            CompilationStart.RegisterOperationAction(
                AnalyzeObjectCreation,
                OperationKind.ObjectCreation);

            CompilationStart.RegisterOperationAction(
                AnalyzePropertyReference,
                OperationKind.PropertyReference);

        });
    }

    /// <summary>
    /// ADR-0070: scenes are built only from scene JSON. A C# scene class, or a class that still
    /// carries <c>[StartupScene]</c>, is SCENE206 so an unconverted scene cannot silently disappear from
    /// the game. The Scene Designer still reads JSON scenes through the same C# DSL internally; that
    /// designer view never enters the build.
    /// </summary>
    private static void AnalyzeNamedType(SymbolAnalysisContext Context){
        var Type=(INamedTypeSymbol)Context.Symbol;
        (var IsStartupScene,_)=SceneDeclarationAnalyzer.ReadStartupScene(Type);
        if(!IsStartupScene&&!SceneTypePredicates.ImplementsSceneInterface(Type)){
            return;
        }

        Context.ReportDiagnostic(Diagnostic.Create(
            SceneJsonDiagnostics.CSharpSceneNotBuilt,
            Type.Locations.FirstOrDefault(),
            SceneDeclarationAnalyzer.ComputeClassFullyQualifiedName(Type)));
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
}
