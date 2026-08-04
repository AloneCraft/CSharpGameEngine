using System;
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
        SceneFile scene = SceneFile.Parse(SampleJson);

        Assert.Equal(2, scene.Nodes.Count);

        SceneFileNode root = scene.Nodes[0];
        Assert.Equal("Root", root.Name);
        Assert.Null(root.ParentId);
        Assert.Null(root.MeshAssetPath);
        Assert.Equal(Vector3.Zero, root.Position);

        SceneFileNode box = scene.Nodes[1];
        Assert.Equal("Box", box.Name);
        Assert.Equal(root.Id, box.ParentId);
        Assert.Equal("Assets/Fab/SomeAsset/model.glb", box.MeshAssetPath);
        Assert.Equal(new Vector3(2, 0, 0), box.Position);
    }

    [Fact]
    public void BuildTree_EditorSpecSample_NestsBoxUnderRoot()
    {
        SceneFile scene = SceneFile.Parse(SampleJson);

        var roots = scene.BuildTree();

        SceneFileNodeTreeItem rootItem = Assert.Single(roots);
        Assert.Equal("Root", rootItem.Node.Name);

        SceneFileNodeTreeItem boxItem = Assert.Single(rootItem.Children);
        Assert.Equal("Box", boxItem.Node.Name);
        Assert.Empty(boxItem.Children);
    }

    [Fact]
    public void ComputeWorldTransforms_ChildOfIdentityRoot_EqualsItsOwnLocalTransform()
    {
        SceneFile scene = SceneFile.Parse(SampleJson);

        var worldTransforms = scene.ComputeWorldTransforms();

        SceneFileNode box = scene.Nodes[1];
        Assert.Equal(box.LocalTransform, worldTransforms[box.Id]);
    }

    [Fact]
    public void BuildTree_MutualCycleWithNoValidRoot_Throws()
    {
        Guid idA = Guid.NewGuid();
        Guid idB = Guid.NewGuid();
        string json = $$"""
            {
              "schemaVersion": 1,
              "nodes": [
                {
                  "id": "{{idA:D}}",
                  "name": "A",
                  "parentId": "{{idB:D}}",
                  "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] }
                },
                {
                  "id": "{{idB:D}}",
                  "name": "B",
                  "parentId": "{{idA:D}}",
                  "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] }
                }
              ]
            }
            """;

        SceneFile scene = SceneFile.Parse(json);

        Assert.Throws<InvalidOperationException>(() => scene.BuildTree());
    }
}
