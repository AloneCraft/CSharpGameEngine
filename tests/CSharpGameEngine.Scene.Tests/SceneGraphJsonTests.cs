using CSharpGameEngine.Core;
using CSharpGameEngine.Scene;

namespace CSharpGameEngine.Scene.Tests;

public class SceneGraphJsonTests
{
    // Verbatim sample from Docs/EditorSpec.md §3.
    private const string SampleJson = """
        {
          "schemaVersion": 1,
          "nodes": [
            {
              "id": "5b1c8e2a-0000-4000-8000-000000000001",
              "name": "Root",
              "parentId": null,
              "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "meshAssetPath": null,
              "components": []
            },
            {
              "id": "5b1c8e2a-0000-4000-8000-000000000002",
              "name": "Box",
              "parentId": "5b1c8e2a-0000-4000-8000-000000000001",
              "transform": { "position": [2, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "meshAssetPath": "Assets/Fab/SomeAsset/model.glb",
              "components": ["UserGameProject.Scripts.SpinBox"]
            }
          ]
        }
        """;

    [Fact]
    public void Parse_EditorSpecSample_ProducesExpectedNodes()
    {
        SceneGraph scene = SceneGraph.Parse(SampleJson);

        Assert.Equal(2, scene.Nodes.Count);

        SceneNode root = scene.Nodes[0];
        Assert.Equal("Root", root.Name);
        Assert.Null(root.ParentId);
        Assert.Null(root.MeshAssetPath);
        Assert.Empty(root.Components);
        Assert.Equal(System.Numerics.Vector3.Zero, root.Transform.Position);
        Assert.Equal(System.Numerics.Quaternion.Identity, root.Transform.Rotation);
        Assert.Equal(System.Numerics.Vector3.One, root.Transform.Scale);

        SceneNode box = scene.Nodes[1];
        Assert.Equal("Box", box.Name);
        Assert.Equal(root.Id, box.ParentId);
        Assert.Equal("Assets/Fab/SomeAsset/model.glb", box.MeshAssetPath);
        Assert.Equal(["UserGameProject.Scripts.SpinBox"], box.Components);
        Assert.Equal(new System.Numerics.Vector3(2, 0, 0), box.Transform.Position);
    }

    [Fact]
    public void RoundTrip_ThroughToJsonAndBackToParse_PreservesAllFields()
    {
        SceneGraph original = SceneGraph.Parse(SampleJson);

        string json = original.ToJson();
        SceneGraph roundTripped = SceneGraph.Parse(json);

        Assert.Equal(original.Nodes.Count, roundTripped.Nodes.Count);
        for (int i = 0; i < original.Nodes.Count; i++)
        {
            SceneNode expected = original.Nodes[i];
            SceneNode actual = roundTripped.Nodes[i];

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.ParentId, actual.ParentId);
            Assert.Equal(expected.MeshAssetPath, actual.MeshAssetPath);
            Assert.Equal(expected.Components, actual.Components);
            Assert.Equal(expected.Transform.Position, actual.Transform.Position);
            Assert.Equal(expected.Transform.Rotation, actual.Transform.Rotation);
            Assert.Equal(expected.Transform.Scale, actual.Transform.Scale);
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsThroughARealFile()
    {
        SceneGraph original = SceneGraph.Parse(SampleJson);
        string path = Path.Combine(Path.GetTempPath(), $"csge-scene-roundtrip-{Guid.NewGuid():N}.scene.json");
        try
        {
            original.Save(path);
            SceneGraph loaded = SceneGraph.Load(path);

            Assert.Equal(original.Nodes.Count, loaded.Nodes.Count);
            Assert.Equal(original.Nodes[1].Name, loaded.Nodes[1].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
