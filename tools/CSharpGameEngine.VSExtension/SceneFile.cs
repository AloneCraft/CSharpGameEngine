using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX-local, independent reader for <c>*.scene.json</c> (Docs/EditorSpec.md §3, ADR-0020).
/// Deliberately does not reference <c>CSharpGameEngine.Scene</c> - that project is net10.0 and
/// cannot be referenced from this net472 project (same TFM-compatibility wall as
/// <c>CSharpGameEngine.Ecs</c>) - so this duplicates the small node/tree model instead, mirroring
/// the existing <c>SceneDocument.cs</c> "thin VSIX-local DTO" convention. Uses plain <see cref="Guid"/>
/// rather than <c>CSharpGameEngine.Core.StableId</c> for the same reason.
/// </summary>
internal sealed class SceneFileNode
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Null for a root node.</summary>
    public Guid? ParentId { get; init; }

    public required Vector3 Position { get; init; }

    public required Quaternion Rotation { get; init; }

    public required Vector3 Scale { get; init; }

    /// <summary>Relative path (from the containing scene file's directory) to a .glb/.gltf/.fbx. Null for a mesh-less node.</summary>
    public string? MeshAssetPath { get; init; }

    public Matrix4x4 LocalTransform =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
}

/// <summary>One <see cref="SceneFileNode"/> plus its resolved children, produced by <see cref="SceneFile.BuildTree"/>.</summary>
internal sealed class SceneFileNodeTreeItem
{
    public required SceneFileNode Node { get; init; }

    public required IReadOnlyList<SceneFileNodeTreeItem> Children { get; init; }
}

internal sealed class SceneFile
{
    public required IReadOnlyList<SceneFileNode> Nodes { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SceneFile Load(string path) => Parse(File.ReadAllText(path));

    public static SceneFile Parse(string json)
    {
        JsonDocumentDto document = JsonSerializer.Deserialize<JsonDocumentDto>(json, JsonOptions)
            ?? throw new InvalidDataException("シーンJSONの読み込みに失敗しました。");

        List<SceneFileNode> nodes = new(document.Nodes.Count);
        foreach (JsonNodeDto node in document.Nodes)
        {
            nodes.Add(new SceneFileNode
            {
                Id = Guid.ParseExact(node.Id, "D"),
                Name = node.Name,
                ParentId = node.ParentId is null ? null : Guid.ParseExact(node.ParentId, "D"),
                Position = ToVector3(node.Transform.Position),
                Rotation = ToQuaternion(node.Transform.Rotation),
                Scale = ToVector3(node.Transform.Scale),
                MeshAssetPath = node.MeshAssetPath,
            });
        }

        return new SceneFile { Nodes = nodes };
    }

    /// <summary>
    /// Resolves <see cref="SceneFileNode.ParentId"/> into a tree; a node with a null/missing parent
    /// is a root (mirrors <c>CSharpGameEngine.Scene.SceneNodeTree</c>'s rule exactly, ADR-0020).
    /// Throws if any node is unreachable from every root (a cycle, or a reference that never
    /// resolves to a root) rather than silently dropping it from the Hierarchy view.
    /// </summary>
    /// <summary>
    /// Resolves every node's world-space transform by walking its parent chain (child's
    /// <see cref="SceneFileNode.LocalTransform"/> composed with the parent's already-resolved
    /// world transform). Guards against cycles independently of <see cref="BuildTree"/> (via a
    /// "currently resolving" set, so callers who skip <see cref="BuildTree"/> can't hang on a
    /// corrupt file) since this is on the rendering path, not just the Hierarchy display path.
    /// </summary>
    public IReadOnlyDictionary<Guid, Matrix4x4> ComputeWorldTransforms()
    {
        Dictionary<Guid, SceneFileNode> byId = [];
        foreach (SceneFileNode node in Nodes)
        {
            byId[node.Id] = node;
        }

        Dictionary<Guid, Matrix4x4> resolved = [];
        HashSet<Guid> visiting = [];
        foreach (SceneFileNode node in Nodes)
        {
            ResolveWorldTransform(node, byId, resolved, visiting);
        }

        return resolved;
    }

    private static Matrix4x4 ResolveWorldTransform(SceneFileNode node, Dictionary<Guid, SceneFileNode> byId, Dictionary<Guid, Matrix4x4> resolved, HashSet<Guid> visiting)
    {
        if (resolved.TryGetValue(node.Id, out Matrix4x4 cached))
        {
            return cached;
        }

        if (!visiting.Add(node.Id))
        {
            throw new InvalidOperationException($"シーンノードの親子関係に循環参照が検出されました(ノードId: {node.Id})。");
        }

        Matrix4x4 world = node.ParentId is Guid parentId && byId.TryGetValue(parentId, out SceneFileNode? parent)
            ? node.LocalTransform * ResolveWorldTransform(parent, byId, resolved, visiting)
            : node.LocalTransform;

        visiting.Remove(node.Id);
        resolved[node.Id] = world;
        return world;
    }

    public IReadOnlyList<SceneFileNodeTreeItem> BuildTree()
    {
        Dictionary<Guid, SceneFileNode> byId = [];
        foreach (SceneFileNode node in Nodes)
        {
            byId[node.Id] = node;
        }

        Dictionary<Guid, List<SceneFileNode>> childrenByParent = [];
        foreach (SceneFileNode node in Nodes)
        {
            if (node.ParentId is Guid parentId && byId.ContainsKey(parentId))
            {
                if (!childrenByParent.TryGetValue(parentId, out List<SceneFileNode>? siblings))
                {
                    siblings = [];
                    childrenByParent[parentId] = siblings;
                }

                siblings.Add(node);
            }
        }

        HashSet<Guid> visited = [];
        List<SceneFileNodeTreeItem> roots = [];
        foreach (SceneFileNode node in Nodes)
        {
            bool isRoot = node.ParentId is null || !byId.ContainsKey(node.ParentId.Value);
            if (isRoot)
            {
                roots.Add(BuildItem(node, childrenByParent, visited));
            }
        }

        if (visited.Count != Nodes.Count)
        {
            throw new InvalidOperationException(
                $"シーンノードの親子関係に循環参照または解決不能な参照があります({Nodes.Count - visited.Count}件のノードがツリーに到達できません)。");
        }

        return roots;
    }

    private static SceneFileNodeTreeItem BuildItem(SceneFileNode node, Dictionary<Guid, List<SceneFileNode>> childrenByParent, HashSet<Guid> visited)
    {
        visited.Add(node.Id);

        List<SceneFileNodeTreeItem> children = [];
        if (childrenByParent.TryGetValue(node.Id, out List<SceneFileNode>? childNodes))
        {
            foreach (SceneFileNode child in childNodes)
            {
                children.Add(BuildItem(child, childrenByParent, visited));
            }
        }

        return new SceneFileNodeTreeItem { Node = node, Children = children };
    }

    private static Vector3 ToVector3(float[] v) => new(v[0], v[1], v[2]);

    private static Quaternion ToQuaternion(float[] v) => new(v[0], v[1], v[2], v[3]);

    private sealed class JsonDocumentDto
    {
        public int SchemaVersion { get; set; }

        public List<JsonNodeDto> Nodes { get; set; } = [];
    }

    private sealed class JsonNodeDto
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string? ParentId { get; set; }

        public JsonTransformDto Transform { get; set; } = new();

        public string? MeshAssetPath { get; set; }
    }

    private sealed class JsonTransformDto
    {
        public float[] Position { get; set; } = [0f, 0f, 0f];

        public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];

        public float[] Scale { get; set; } = [1f, 1f, 1f];
    }
}
