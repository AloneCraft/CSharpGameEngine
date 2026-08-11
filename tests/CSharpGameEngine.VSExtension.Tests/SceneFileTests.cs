using System;
using System.IO;
using System.Numerics;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Exercises the VSIX-local <see cref="SceneFile"/> parser/tree-builder directly (no VS dependency,
/// same pattern as <see cref="FabDownloadWatcherTests"/>). Uses the same verbatim EditorSpec §3
/// sample JSON as <c>CSharpGameEngine.Scene.Tests</c>' equivalent tests, so the two independent
/// implementations (necessarily separate due to the net472/net10.0 TFM wall, ADR-0020) are checked
/// against identical input rather than silently drifting apart.
/// </summary>
public class SceneFileTests
{
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
        SceneFile Scene = SceneFile.Parse(SampleJson);

        Assert.Equal(2, Scene.Nodes.Count);

        SceneFileNode Root = Scene.Nodes[0];
        Assert.Equal("Root", Root.Name);
        Assert.Null(Root.ParentId);
        Assert.Null(Root.MeshAssetPath);
        Assert.Equal(Vector3.Zero, Root.Position);

        SceneFileNode Box = Scene.Nodes[1];
        Assert.Equal("Box", Box.Name);
        Assert.Equal(Root.Id, Box.ParentId);
        Assert.Equal("Assets/Fab/SomeAsset/model.glb", Box.MeshAssetPath);
        Assert.Equal(new Vector3(2, 0, 0), Box.Position);
    }

    [Fact]
    public void BuildTree_EditorSpecSample_NestsBoxUnderRoot()
    {
        SceneFile Scene = SceneFile.Parse(SampleJson);

        var Roots = Scene.BuildTree();

        SceneFileNodeTreeItem RootItem = Assert.Single(Roots);
        Assert.Equal("Root", RootItem.Node.Name);

        SceneFileNodeTreeItem BoxItem = Assert.Single(RootItem.Children);
        Assert.Equal("Box", BoxItem.Node.Name);
        Assert.Empty(BoxItem.Children);
    }

    [Fact]
    public void ComputeWorldTransforms_ChildOfIdentityRoot_EqualsItsOwnLocalTransform()
    {
        SceneFile Scene = SceneFile.Parse(SampleJson);

        var WorldTransforms = Scene.ComputeWorldTransforms();

        SceneFileNode Box = Scene.Nodes[1];
        Assert.Equal(Box.LocalTransform, WorldTransforms[Box.Id]);
    }

    [Fact]
    public void BuildTree_MutualCycleWithNoValidRoot_Throws()
    {
        Guid IdA = Guid.NewGuid();
        Guid IdB = Guid.NewGuid();
        string Json = $$"""
            {
              "schemaVersion": 1,
              "nodes": [
                {
                  "id": "{{IdA:D}}",
                  "name": "A",
                  "parentId": "{{IdB:D}}",
                  "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] }
                },
                {
                  "id": "{{IdB:D}}",
                  "name": "B",
                  "parentId": "{{IdA:D}}",
                  "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] }
                }
              ]
            }
            """;

        SceneFile Scene = SceneFile.Parse(Json);

        Assert.Throws<InvalidOperationException>(() => Scene.BuildTree());
    }

    [Fact]
    public void ToJson_ThenParse_RoundTripsAllFields()
    {
        SceneFile Original = SceneFile.Parse(SampleJson);

        string Json = Original.ToJson();
        SceneFile RoundTripped = SceneFile.Parse(Json);

        Assert.Equal(Original.Nodes.Count, RoundTripped.Nodes.Count);
        for (int i = 0; i < Original.Nodes.Count; i++)
        {
            SceneFileNode Expected = Original.Nodes[i];
            SceneFileNode Actual = RoundTripped.Nodes[i];

            Assert.Equal(Expected.Id, Actual.Id);
            Assert.Equal(Expected.Name, Actual.Name);
            Assert.Equal(Expected.ParentId, Actual.ParentId);
            Assert.Equal(Expected.MeshAssetPath, Actual.MeshAssetPath);
            Assert.Equal(Expected.Position, Actual.Position);
            Assert.Equal(Expected.Rotation, Actual.Rotation);
            Assert.Equal(Expected.Scale, Actual.Scale);
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsThroughARealFile()
    {
        SceneFile Original = SceneFile.Parse(SampleJson);
        string path = Path.Combine(Path.GetTempPath(), $"csge-scenefile-roundtrip-{Guid.NewGuid():N}.scene");
        try
        {
            Original.Save(path);
            SceneFile Loaded = SceneFile.Load(path);

            Assert.Equal(Original.Nodes.Count, Loaded.Nodes.Count);
            Assert.Equal(Original.Nodes[1].Name, Loaded.Nodes[1].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AddNode_ReturnsNewInstanceWithNodeAppended_OriginalUnchanged()
    {
        SceneFile Original = SceneFile.Parse(SampleJson);
        SceneFileNode NewNode = new()
        {
            Id = Guid.NewGuid(),
            Name = "Sphere",
            ParentId = Original.FindFirstRootNodeId(),
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            MeshAssetPath = "Assets/Primitives/Sphere.glb",
        };

        SceneFile Updated = Original.AddNode(NewNode);

        Assert.Equal(2, Original.Nodes.Count);
        Assert.Equal(3, Updated.Nodes.Count);
        Assert.Contains(Updated.Nodes, N => N.Name == "Sphere");
    }

    [Fact]
    public void FindFirstRootNodeId_ReturnsTheNodeWithNullParentId()
    {
        SceneFile Scene = SceneFile.Parse(SampleJson);

        Guid? RootId = Scene.FindFirstRootNodeId();

        Assert.Equal(Scene.Nodes[0].Id, RootId);
    }

    [Fact]
    public void ContainsNodeNamed_MatchesExistingNamesOnly()
    {
        SceneFile Scene = SceneFile.Parse(SampleJson);

        Assert.True(Scene.ContainsNodeNamed("Box"));
        Assert.False(Scene.ContainsNodeNamed("Sphere"));
    }
}
