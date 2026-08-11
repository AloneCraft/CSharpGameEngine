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
        ScenePreviewBridgeDocument Document = new()
        {
            Success = true,
            SceneClassFullName = "Sample.MainScene",
            Nodes =
            [
                new ScenePreviewNodeDto { EntityId = 0, ParentEntityId = null, Name = "Root", Position = [0f, 0f, 0f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
                new ScenePreviewNodeDto { EntityId = 1, ParentEntityId = 0, Name = "MainCamera", Position = [0f, 2f, -10f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
            ],
        };

        SceneFile Scene = ScenePreviewSceneFileAdapter.ToSceneFile(Document);

        Assert.Equal(2, Scene.Nodes.Count);
        SceneFileNode Root = Scene.Nodes.Single(N => N.Name == "Root");
        SceneFileNode MainCamera = Scene.Nodes.Single(N => N.Name == "MainCamera");
        Assert.Null(Root.ParentId);
        Assert.Equal(Root.Id, MainCamera.ParentId);
        Assert.Equal(new Vector3(0f, 2f, -10f), MainCamera.Position);
    }

    [Fact]
    public void ToSceneFile_EveryNodeHasNullMeshAssetPath()
    {
        ScenePreviewBridgeDocument Document = new()
        {
            Success = true,
            Nodes = [new ScenePreviewNodeDto { EntityId = 0, Name = "Root" }],
        };

        SceneFile Scene = ScenePreviewSceneFileAdapter.ToSceneFile(Document);

        Assert.All(Scene.Nodes, N => Assert.Null(N.MeshAssetPath));
    }

    [Fact]
    public void ToSceneFile_MissingName_FallsBackToEntityIdLabel()
    {
        ScenePreviewBridgeDocument Document = new()
        {
            Success = true,
            Nodes = [new ScenePreviewNodeDto { EntityId = 5, Name = null }],
        };

        SceneFile Scene = ScenePreviewSceneFileAdapter.ToSceneFile(Document);

        Assert.Equal("Entity5", Assert.Single(Scene.Nodes).Name);
    }

    [Fact]
    public void ToSceneFile_ResultBuildsTreeAndComputesWorldTransforms()
    {
        ScenePreviewBridgeDocument Document = new()
        {
            Success = true,
            Nodes =
            [
                new ScenePreviewNodeDto { EntityId = 0, ParentEntityId = null, Name = "Root", Position = [0f, 0f, 0f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
                new ScenePreviewNodeDto { EntityId = 1, ParentEntityId = 0, Name = "Child", Position = [1f, 0f, 0f], Rotation = [0f, 0f, 0f, 1f], Scale = [1f, 1f, 1f] },
            ],
        };

        SceneFile Scene = ScenePreviewSceneFileAdapter.ToSceneFile(Document);

        IReadOnlyList<SceneFileNodeTreeItem> Tree = Scene.BuildTree();
        SceneFileNodeTreeItem Root = Assert.Single(Tree);
        Assert.Equal("Root", Root.Node.Name);
        SceneFileNodeTreeItem Child = Assert.Single(Root.Children);
        Assert.Equal("Child", Child.Node.Name);

        IReadOnlyDictionary<System.Guid, Matrix4x4> WorldTransforms = Scene.ComputeWorldTransforms();
        Assert.Equal(2, WorldTransforms.Count);
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
        string First = ScenePreviewSceneFileAdapter.ResolveTempScenePath("Sample.MainScene");
        string Second = ScenePreviewSceneFileAdapter.ResolveTempScenePath("Sample.MainScene");

        Assert.Equal(First, Second);
    }
}
