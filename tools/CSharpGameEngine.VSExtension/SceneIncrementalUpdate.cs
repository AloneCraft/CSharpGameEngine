using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

internal enum SceneRenderUpdateKind {
    None,
    Incremental,
    Rebuild,
}

/// <summary>SceneDiff を renderer 更新の最小単位へ分類する。</summary>
internal static class SceneIncrementalUpdatePolicy {
    internal static SceneRenderUpdateKind Classify(SceneDiff Diff){
        if(Diff is null){ throw new ArgumentNullException(nameof(Diff)); }
        if(Diff.IsEmpty){ return SceneRenderUpdateKind.None; }

        foreach(SceneDiffEntry Entry in Diff.Entries){
            if(Entry.Kind is not(SceneDiffKind.ComponentChanged or SceneDiffKind.MemberChanged)){
                return SceneRenderUpdateKind.Rebuild;
            }
            if(Entry.ComponentType is null){ return SceneRenderUpdateKind.Rebuild; }
            if(SceneComponentBindings.TryGetRole(Entry.ComponentType,out SceneComponentRole Role)
                &&Role is SceneComponentRole.PrimitiveMesh or SceneComponentRole.StaticMesh){
                return SceneRenderUpdateKind.Rebuild;
            }
        }

        return SceneRenderUpdateKind.Incremental;
    }
}

/// <summary>mesh/GPU resource を維持したまま、全ノードの world transform を再計算する。</summary>
internal static class SceneRenderIncrementalUpdater {
    internal static bool TryUpdateTransforms(
        SceneModel Model,IReadOnlyList<SceneRenderObject>Existing,
        out IReadOnlyList<SceneRenderObject>Updated){
        if(Model?.Root is null){
            Updated=Array.Empty<SceneRenderObject>();
            return false;
        }

        Dictionary<NodePath,Matrix4x4>WorldByPath=[];
        Append(Model.Root,Matrix4x4.Identity);
        if(WorldByPath.Count!=Existing.Count
            ||Existing.Any(Object=>!WorldByPath.ContainsKey(Object.NodeId))){
            Updated=Array.Empty<SceneRenderObject>();
            return false;
        }

        Updated=Existing.Select(Object=>new SceneRenderObject {
            Mesh=Object.Mesh,
            World=WorldByPath[Object.NodeId],
            NodeId=Object.NodeId,
            IncludeInBounds=Object.IncludeInBounds,
            IsPickable=Object.IsPickable,
        }).ToArray();
        return true;

        void Append(SceneNode Node,Matrix4x4 ParentWorld){
            Matrix4x4 World=(SceneTransformBinding.TryRead(Node,out SceneTransformValue Local)
                ?SceneTransformMath.ToMatrix(Local)
                :Matrix4x4.Identity)*ParentWorld;
            WorldByPath.Add(Node.Path,World);
            foreach(SceneNode Child in Node.Children){ Append(Child,World); }
        }
    }
}
