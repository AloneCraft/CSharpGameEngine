using System.Text.Json;

namespace CSharpGameEngine.Scene;

/// <summary>
/// On-disk JSON shape for a <c>*.scene.json</c> file (Docs/EditorSpec.md §3). Kept as plain,
/// independent POCOs (not the public <see cref="SceneNode"/>/<see cref="Transform"/> model) so the
/// id/vector conversions in <see cref="SceneGraph"/> stay explicit rather than relying on custom
/// converters for <see cref="CSharpGameEngine.Core.StableId"/>/<see cref="System.Numerics.Vector3"/>.
/// </summary>
internal sealed class SceneJsonDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<SceneJsonNode> Nodes { get; set; } = [];
}

internal sealed class SceneJsonNode
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? ParentId { get; set; }

    public SceneJsonTransform Transform { get; set; } = new();

    public string? MeshAssetPath { get; set; }

    public List<string> Components { get; set; } = [];
}

internal sealed class SceneJsonTransform
{
    public float[] Position { get; set; } = [0f, 0f, 0f];

    public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];

    public float[] Scale { get; set; } = [1f, 1f, 1f];
}

internal static class SceneJsonOptions
{
    public static readonly JsonSerializerOptions Value = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
