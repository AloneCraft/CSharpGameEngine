using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Converts a <see cref="ScenePreviewBridgeDocument"/> (flat <c>EntityId</c>/<c>ParentEntityId</c>
/// nodes from the bridge) into a <see cref="SceneFile"/> - the shape the existing シーン preview
/// pipeline already consumes (ADR-0031). Every node gets <c>MeshAssetPath = null</c> (C#-authored
/// nodes have no mesh concept at all), so every one flows through <see cref="SceneHwndHost"/>'s gizmo
/// fallback.
/// </summary>
internal static class ScenePreviewSceneFileAdapter {
    internal static SceneFile ToSceneFile(ScenePreviewBridgeDocument Document){
        Dictionary<int,Guid>IdsByEntityId=Document.Nodes.ToDictionary(N=>N.EntityId,_=>Guid.NewGuid());

        List<SceneFileNode>Nodes=[];
        foreach(ScenePreviewNodeDto Node in Document.Nodes){
            Nodes.Add(new SceneFileNode {
                Id=IdsByEntityId[Node.EntityId],
                Name=Node.Name?? $"Entity{Node.EntityId}",
                ParentId=Node.ParentEntityId is int ParentEntityId?IdsByEntityId[ParentEntityId]:null,
                Position=ToVector3(Node.Position),
                Rotation=ToQuaternion(Node.Rotation),
                Scale=ToVector3(Node.Scale),
                MeshAssetPath=null,
            });
        }

        return new SceneFile { Nodes=Nodes };
    }

    /// <summary>Deterministic per-scene-class temp path, outside the repo tree entirely - re-previewing the same class overwrites the same file rather than accumulating one per click.</summary>
    internal static string ResolveTempScenePath(string SceneClassFullName){
        char[]Sanitized=[.. SceneClassFullName.Select(C=>char.IsLetterOrDigit(C)||C=='.'?C:'_')];
        var directory=Path.Combine(Path.GetTempPath(),"CSharpGameEngine.ScenePreview");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory,new string(Sanitized)+".scene");
    }

    private static Vector3 ToVector3(float[]V)=>V.Length==3?new Vector3(V[0],V[1],V[2]):Vector3.Zero;

    private static Quaternion ToQuaternion(float[]V)=>V.Length==4?new Quaternion(V[0],V[1],V[2],V[3]):Quaternion.Identity;
}
