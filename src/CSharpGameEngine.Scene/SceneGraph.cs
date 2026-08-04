using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Scene;

/// <summary>
/// A scene: a flat list of <see cref="SceneNode"/>, each optionally pointing at a parent via
/// <see cref="SceneNode.ParentId"/> (Docs/EditorSpec.md §3). Named <c>SceneGraph</c> rather than
/// <c>Scene</c> to avoid a type sharing its simple name with this namespace's last segment.
/// </summary>
public sealed class SceneGraph
{
    public required IReadOnlyList<SceneNode> Nodes { get; init; }

    public static SceneGraph Load(string path) => Parse(File.ReadAllText(path));

    public static SceneGraph Parse(string json)
    {
        SceneJsonDocument document = JsonSerializer.Deserialize<SceneJsonDocument>(json, SceneJsonOptions.Value)
            ?? throw new InvalidDataException("シーンJSONの読み込みに失敗しました。");

        List<SceneNode> nodes = new(document.Nodes.Count);
        foreach (SceneJsonNode jsonNode in document.Nodes)
        {
            nodes.Add(new SceneNode
            {
                Id = StableId.Parse(jsonNode.Id),
                Name = jsonNode.Name,
                ParentId = jsonNode.ParentId is null ? null : StableId.Parse(jsonNode.ParentId),
                Transform = new Transform(
                    ToVector3(jsonNode.Transform.Position),
                    ToQuaternion(jsonNode.Transform.Rotation),
                    ToVector3(jsonNode.Transform.Scale)),
                MeshAssetPath = jsonNode.MeshAssetPath,
                Components = jsonNode.Components,
            });
        }

        return new SceneGraph { Nodes = nodes };
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public string ToJson()
    {
        SceneJsonDocument document = new()
        {
            SchemaVersion = 1,
            Nodes = [.. Nodes.Select(ToJsonNode)],
        };

        return JsonSerializer.Serialize(document, SceneJsonOptions.Value);
    }

    public SceneNodeTree BuildTree() => SceneNodeTree.Build(Nodes);

    private static SceneJsonNode ToJsonNode(SceneNode node) => new()
    {
        Id = node.Id.ToString(),
        Name = node.Name,
        ParentId = node.ParentId?.ToString(),
        Transform = new SceneJsonTransform
        {
            Position = FromVector3(node.Transform.Position),
            Rotation = FromQuaternion(node.Transform.Rotation),
            Scale = FromVector3(node.Transform.Scale),
        },
        MeshAssetPath = node.MeshAssetPath,
        Components = [.. node.Components],
    };

    private static Vector3 ToVector3(float[] v) => new(v[0], v[1], v[2]);

    private static Quaternion ToQuaternion(float[] v) => new(v[0], v[1], v[2], v[3]);

    private static float[] FromVector3(Vector3 v) => [v.X, v.Y, v.Z];

    private static float[] FromQuaternion(Quaternion q) => [q.X, q.Y, q.Z, q.W];
}
