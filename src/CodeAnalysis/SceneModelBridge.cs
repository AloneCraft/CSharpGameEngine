using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// シーンJSON文書から<see cref="SceneEmitModel"/>を生成する（ADR-0070）。シーン解析とコード生成の間にある唯一の境界。
///
/// <para>
/// <b>シンボルを保持するオブジェクトをこのクラスの外へ渡さない。</b><c>ISymbol</c>は<c>Compilation</c>をルート参照し、値の等価性も持たない。
/// そのため<c>IncrementalValuesProvider</c>を経由させるとキャッシュが機能せず、メモリリークも起きる。
/// </para>
///
/// <para>
/// <b>診断は報告しない。</b>文書の問題は<see cref="SceneJsonDslAnalyzer"/>が報告する。ここではそれらの問題を、シーンを出力するかどうかの
/// 判定にのみ使う（<see cref="SceneEmitModel.HasErrors"/>）。
/// </para>
/// </summary>
internal static class SceneModelBridge {
    /// <summary>ADR-0070：シーンJSON文書。エンジンプロジェクト外、またはテキストがない場合は<c>null</c>。</summary>
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

    /// <summary>宣言済みコンポーネントと、生成処理が追加するコンポーネント。</summary>
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

        // サンプル実行ファイルは、生成時にレンダリング可能なSceneノードをグローバルインデックスへ登録する。
        // 生成するコンポーネントは明示的かつ決定的である。実行時のメッシュ境界を推測して追加することはない。
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
