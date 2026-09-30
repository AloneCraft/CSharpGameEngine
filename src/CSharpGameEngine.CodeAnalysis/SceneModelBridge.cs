using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Turns a scene JSON document into a <see cref="SceneEmitModel"/> (ADR-0070): the only
/// boundary between scene analysis and generation.
///
/// <para>
/// <b>Nothing holding symbols leaves this class.</b> <c>ISymbol</c> roots its <c>Compilation</c> and has
/// no value equality, so flowing it through an <c>IncrementalValuesProvider</c> would defeat caching
/// and leak memory.
/// </para>
///
/// <para>
/// <b>It reports no diagnostics.</b> <see cref="SceneJsonDslAnalyzer"/> reports the document's problems;
/// here they only decide whether the scene is emitted (<see cref="SceneEmitModel.HasErrors"/>).
/// </para>
/// </summary>
internal static class SceneModelBridge {
    /// <summary>ADR-0070: a scene JSON document; <c>null</c> outside engine projects or without text.</summary>
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

    /// <summary>The declared components plus the ones generation adds.</summary>
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
