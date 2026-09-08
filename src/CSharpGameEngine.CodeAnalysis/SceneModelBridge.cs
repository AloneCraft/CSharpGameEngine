using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
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

    private static SceneEmitNode ToEmitNode(SceneNode Node,bool AddSampleSpatialBounds){
        var Components=ImmutableArray.CreateBuilder<SceneEmitComponent>(Node.Components.Length+1);

        bool HasTransform=false;
        bool HasRenderableMesh=false;
        bool HasSpatialBounds=false;
        foreach(var Component in Node.Components){
            Components.Add(new SceneEmitComponent(Component.TypeFullyQualifiedName,Component.EmitExpressionText));
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

        var Children=ImmutableArray.CreateBuilder<SceneEmitNode>(Node.Children.Length);

        foreach(var Child in Node.Children){
            Children.Add(ToEmitNode(Child,AddSampleSpatialBounds));
        }

        return new SceneEmitNode(
            BuildLocalName(Node.Path),
            Node.Path.ToDisplayString(),
            Node.Name,
            Components.ToImmutable(),
            Children.ToImmutable());
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
