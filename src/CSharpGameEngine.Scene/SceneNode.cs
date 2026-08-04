using CSharpGameEngine.Core;

namespace CSharpGameEngine.Scene;

/// <summary>
/// One node in a <see cref="SceneGraph"/> (Docs/EditorSpec.md §3): a Unity/UE-style tree entry with
/// a stable identity, an optional parent, a local transform, and an optional mesh asset reference.
/// </summary>
public sealed class SceneNode
{
    public required StableId Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Null for a root node.</summary>
    public StableId? ParentId { get; init; }

    public required Transform Transform { get; init; }

    /// <summary>Relative path (from the containing <c>*.scene.json</c> file's directory) to a .glb/.gltf/.fbx. Null for a mesh-less node.</summary>
    public string? MeshAssetPath { get; init; }

    /// <summary>Fully-qualified type names of attached C# scripts (Docs/EditorSpec.md Phase 3 - unused so far).</summary>
    public IReadOnlyList<string> Components { get; init; } = [];
}
