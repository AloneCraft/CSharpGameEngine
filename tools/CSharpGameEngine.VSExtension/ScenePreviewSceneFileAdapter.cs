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
internal static class ScenePreviewSceneFileAdapter
{
    internal static SceneFile ToSceneFile(ScenePreviewBridgeDocument document)
    {
        Dictionary<int, Guid> idsByEntityId = document.Nodes.ToDictionary(n => n.EntityId, _ => Guid.NewGuid());

        List<SceneFileNode> nodes = [];
        foreach (ScenePreviewNodeDto node in document.Nodes)
        {
            nodes.Add(new SceneFileNode
            {
                Id = idsByEntityId[node.EntityId],
                Name = node.Name ?? $"Entity{node.EntityId}",
                ParentId = node.ParentEntityId is int parentEntityId ? idsByEntityId[parentEntityId] : null,
                Position = ToVector3(node.Position),
                Rotation = ToQuaternion(node.Rotation),
                Scale = ToVector3(node.Scale),
                MeshAssetPath = null,
            });
        }

        return new SceneFile { Nodes = nodes };
    }

    /// <summary>Deterministic per-scene-class temp path, outside the repo tree entirely - re-previewing the same class overwrites the same file rather than accumulating one per click.</summary>
    internal static string ResolveTempScenePath(string sceneClassFullName)
    {
        char[] sanitized = [.. sceneClassFullName.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_')];
        string directory = Path.Combine(Path.GetTempPath(), "CSharpGameEngine.ScenePreview");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, new string(sanitized) + ".scene");
    }

    private static Vector3 ToVector3(float[] v) => v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;

    private static Quaternion ToQuaternion(float[] v) => v.Length == 4 ? new Quaternion(v[0], v[1], v[2], v[3]) : Quaternion.Identity;
}
