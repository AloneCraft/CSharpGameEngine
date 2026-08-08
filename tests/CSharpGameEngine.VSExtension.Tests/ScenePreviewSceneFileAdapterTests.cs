using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ScenePreviewSceneFileAdapterTests
{
    [Fact]
    public void ToSceneFile_ResolvesParentIdFromParentEntityId()
    {
        ScenePreviewBridgeDocument document = new()
        {
            Success = true,
            SceneClassFullName = "Sample.MainScene",
            Nodes =
            [
                new ScenePreviewNodeDto { EntityId = 0, ParentEntityId = null, Name = "Root", Position = [0f, 0f, 0f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
                new ScenePreviewNodeDto { EntityId = 1, ParentEntityId = 0, Name = "MainCamera", Position = [0f, 2f, -10f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
            ],
        };

        SceneFile scene = ScenePreviewSceneFileAdapter.ToSceneFile(document);

        Assert.Equal(2, scene.Nodes.Count);
        SceneFileNode root = scene.Nodes.Single(n => n.Name == "Root");
        SceneFileNode mainCamera = scene.Nodes.Single(n => n.Name == "MainCamera");
        Assert.Null(root.ParentId);
        Assert.Equal(root.Id, mainCamera.ParentId);
        Assert.Equal(new Vector3(0f, 2f, -10f), mainCamera.Position);
    }

    [Fact]
    public void ToSceneFile_EveryNodeHasNullMeshAssetPath()
    {
        ScenePreviewBridgeDocument document = new()
        {
            Success = true,
            Nodes = [new ScenePreviewNodeDto { EntityId = 0, Name = "Root" }],
        };

        SceneFile scene = ScenePreviewSceneFileAdapter.ToSceneFile(document);

        Assert.All(scene.Nodes, n => Assert.Null(n.MeshAssetPath));
    }

    [Fact]
    public void ToSceneFile_MissingName_FallsBackToEntityIdLabel()
    {
        ScenePreviewBridgeDocument document = new()
        {
            Success = true,
            Nodes = [new ScenePreviewNodeDto { EntityId = 5, Name = null }],
        };

        SceneFile scene = ScenePreviewSceneFileAdapter.ToSceneFile(document);

        Assert.Equal("Entity5", Assert.Single(scene.Nodes).Name);
    }

    [Fact]
    public void ToSceneFile_ResultBuildsTreeAndComputesWorldTransforms()
    {
        ScenePreviewBridgeDocument document = new()
        {
            Success = true,
            Nodes =
            [
                new ScenePreviewNodeDto { EntityId = 0, ParentEntityId = null, Name = "Root", Position = [0f, 0f, 0f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
                new ScenePreviewNodeDto { EntityId = 1, ParentEntityId = 0, Name = "Child", Position = [1f, 0f, 0f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
            ],
        };

        SceneFile scene = ScenePreviewSceneFileAdapter.ToSceneFile(document);

        IReadOnlyList<SceneFileNodeTreeItem> tree = scene.BuildTree();
        SceneFileNodeTreeItem root = Assert.Single(tree);
        Assert.Equal("Root", root.Node.Name);
        SceneFileNodeTreeItem child = Assert.Single(root.Children);
        Assert.Equal("Child", child.Node.Name);

        IReadOnlyDictionary<System.Guid, Matrix4x4> worldTransforms = scene.ComputeWorldTransforms();
        Assert.Equal(2, worldTransforms.Count);
    }

    [Fact]
    public void ResolveTempScenePath_SanitizesNonAlphanumericCharacters_AndEndsWithSceneExtension()
    {
        string path = ScenePreviewSceneFileAdapter.ResolveTempScenePath("Sample.MainScene+Nested");

        Assert.EndsWith("Sample.MainScene_Nested.scene", path);
        Assert.DoesNotContain("+", path);
    }

    [Fact]
    public void ResolveTempScenePath_SameClassName_ReturnsSamePathAcrossCalls()
    {
        string first = ScenePreviewSceneFileAdapter.ResolveTempScenePath("Sample.MainScene");
        string second = ScenePreviewSceneFileAdapter.ResolveTempScenePath("Sample.MainScene");

        Assert.Equal(first, second);
    }
}
