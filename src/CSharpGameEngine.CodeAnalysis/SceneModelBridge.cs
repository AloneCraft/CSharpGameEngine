using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>IScene</c>実装クラスを見つけて<see cref="SceneEmitModel"/>へ落とす、解析層と生成層の唯一の境界
/// (ADR-0040)。
///
/// <para>
/// <b>シンボルを持つ<see cref="SceneModel"/>はこのクラスの中だけで生き、外へ出さない。</b>
/// <c>ISymbol</c>は<c>Compilation</c>をルート保持し値等価性も持たないため、
/// <c>IncrementalValuesProvider</c>へ流すとキャッシュが常時無効化されメモリをリークさせる。
/// </para>
///
/// <para>
/// <b>診断は一切報告しない。</b>SCENE1xxを報告するのは<see cref="SceneDslAnalyzer"/>だけであり、
/// ここでは<see cref="SceneEmitModel.HasErrors"/>として生成可否の判定にのみ使う(二重スクイグルの防止)。
/// </para>
/// </summary>
internal static class SceneModelBridge {
    /// <summary>
    /// 構文レベルの事前判定は共有ソース側に置いてある。VSIXも同じ述語で候補を絞るため、片方だけが
    /// 別の絞り込みをして「SGは見つけるがエディターは見つけない」Sceneが生まれることがない。
    /// </summary>
    internal static bool IsSceneCandidate(SyntaxNode Node)=>SceneDeclarationAnalyzer.IsSceneCandidate(Node);

    internal static SceneEmitModel?TryAnalyze(GeneratorSyntaxContext Ctx,CancellationToken CancellationToken){
        if(Ctx.SemanticModel.GetDeclaredSymbol(Ctx.Node,CancellationToken) is not INamedTypeSymbol Symbol
            ||!SceneTypePredicates.ImplementsSceneInterface(Symbol)){
            return null;
        }

        var Model=SceneDeclarationAnalyzer.Analyze(Symbol,Ctx.SemanticModel.Compilation,CancellationToken);
        return ToEmitModel(Model);
    }

    private static SceneEmitModel ToEmitModel(SceneModel Model){
        return new SceneEmitModel(
            Model.Namespace,
            Model.ClassName,
            Model.ClassFullyQualifiedName,
            Model.Root is { } Root?ToEmitNode(Root,string.Equals(Model.Namespace,"UserEcsProject.Scenes",StringComparison.Ordinal)):null,
            Model.TotalNodeCount,
            Model.IsStartupScene,
            Model.StartupOrder,
            Model.HasErrors,
            Model.FilePath,
            Model.DeclarationPosition);
    }

    /// <summary>ADR-0070: a <c>*.scene.json</c> document; <c>null</c> outside engine projects or without text.</summary>
    internal static SceneEmitModel?TryAnalyzeJson(AdditionalText File,Compilation Compilation,CancellationToken CancellationToken){
        if(Compilation.GetTypeByMetadataName(SceneDslWellKnownNames.SceneInterfaceMetadataName) is null
            ||File.GetText(CancellationToken) is not { }Text){
            return null;
        }
        var Scene=SceneJsonSceneAnalyzer.Analyze(Text.ToString(),Compilation);
        var AddSampleSpatialBounds=string.Equals(Scene.Namespace,"UserEcsProject.Scenes",StringComparison.Ordinal);
        SceneEmitNode?Root=Scene.Root is { }JsonRoot?ToEmitNode(JsonRoot,AddSampleSpatialBounds):null;
        return new SceneEmitModel(
            Scene.Namespace,
            Scene.ClassName,
            Scene.Namespace is null?Scene.ClassName:Scene.Namespace+"."+Scene.ClassName,
            Root,
            Root is { }Emitted?CountNodes(Emitted):0,
            Scene.StartupOrder.HasValue,
            Scene.StartupOrder??0,
            Scene.HasErrors,
            File.Path,
            DeclarationPosition:0);
    }

    private static int CountNodes(SceneEmitNode Node){
        var Count=1;
        foreach(var Child in Node.Children){ Count+=CountNodes(Child); }
        return Count;
    }

    private static SceneEmitNode ToEmitNode(SceneJsonNode Node,bool AddSampleSpatialBounds){
        var Components=WithGeneratedComponents(Node.Components.Select(static Component=>new SceneEmitComponent(Component.TypeFullyQualifiedName,Component.EmitExpressionText)),AddSampleSpatialBounds);
        var Children=ImmutableArray.CreateBuilder<SceneEmitNode>(Node.Children.Length);
        foreach(var Child in Node.Children){
            Children.Add(ToEmitNode(Child,AddSampleSpatialBounds));
        }
        var Path=new NodePath(Node.Path);
        return new SceneEmitNode(BuildLocalName(Path),Path.ToDisplayString(),Node.Name,Components,Children.ToImmutable());
    }

    private static SceneEmitNode ToEmitNode(SceneNode Node,bool AddSampleSpatialBounds){
        var Components=WithGeneratedComponents(Node.Components.Select(static Component=>new SceneEmitComponent(Component.TypeFullyQualifiedName,Component.EmitExpressionText)),AddSampleSpatialBounds);

        var Children=ImmutableArray.CreateBuilder<SceneEmitNode>(Node.Children.Length);

        foreach(var Child in Node.Children){
            Children.Add(ToEmitNode(Child,AddSampleSpatialBounds));
        }

        return new SceneEmitNode(
            BuildLocalName(Node.Path),
            Node.Path.ToDisplayString(),
            Node.Name,
            Components,
            Children.ToImmutable());
    }

    /// <summary>The declared components plus the ones generation adds; shared by C# and JSON scenes.</summary>
    private static ImmutableArray<SceneEmitComponent>WithGeneratedComponents(IEnumerable<SceneEmitComponent>Declared,bool AddSampleSpatialBounds){
        var Components=ImmutableArray.CreateBuilder<SceneEmitComponent>();

        bool HasTransform=false;
        bool HasRenderableMesh=false;
        bool HasSpatialBounds=false;
        foreach(var Component in Declared){
            Components.Add(Component);
            HasTransform|=string.Equals(Component.TypeFullyQualifiedName,"global::CSharpGameEngine.Entities.Transform",StringComparison.Ordinal);
            HasRenderableMesh|=string.Equals(Component.TypeFullyQualifiedName,"global::CSharpGameEngine.Runtime.Windows.StaticMesh",StringComparison.Ordinal)
                ||string.Equals(Component.TypeFullyQualifiedName,"global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh",StringComparison.Ordinal);
            HasSpatialBounds|=string.Equals(Component.TypeFullyQualifiedName,"global::CSharpGameEngine.Entities.SpatialBounds",StringComparison.Ordinal);
        }

        // The sample executable opts its renderable Scene nodes into the global index at generation time.
        // The generated component is explicit and deterministic; runtime mesh bounds are never inferred.
        if(AddSampleSpatialBounds&&HasTransform&&HasRenderableMesh&&!HasSpatialBounds){
            Components.Add(new SceneEmitComponent(
                "global::CSharpGameEngine.Entities.SpatialBounds",
                "global::CSharpGameEngine.Entities.SpatialBounds.UnitCube"));
        }

        return Components.ToImmutable();
    }

    /// <summary>
    /// <c>NodePath</c>から生成コードのローカル変数名を作る(<c>Node</c>/<c>Node_0</c>/<c>Node_0_1</c>)。
    /// <c>Name</c>を使わないのは、識別子である保証がまったく無いため(省略可能で、空白も日本語も入る)。
    /// </summary>
    internal static string BuildLocalName(NodePath Path){
        if(Path.IsRoot){
            return "Node";
        }

        return "Node_"+string.Join("_",Path.Indices);
    }
}
