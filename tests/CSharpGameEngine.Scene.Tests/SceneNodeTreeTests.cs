using CSharpGameEngine.Core;
using CSharpGameEngine.Scene;

namespace CSharpGameEngine.Scene.Tests;

public class SceneNodeTreeTests
{
    private static SceneNode MakeNode(StableId id, string name, StableId? parentId) => new()
    {
        Id = id,
        Name = name,
        ParentId = parentId,
        Transform = Transform.Identity,
    };

    [Fact]
    public void Build_ThreeLevelChain_ResolvesParentChildNesting()
    {
        StableId rootId = StableId.New();
        StableId childId = StableId.New();
        StableId grandchildId = StableId.New();

        SceneNode root = MakeNode(rootId, "Root", null);
        SceneNode child = MakeNode(childId, "A", rootId);
        SceneNode grandchild = MakeNode(grandchildId, "B", childId);

        SceneNodeTree tree = SceneNodeTree.Build([root, child, grandchild]);

        SceneNodeTreeItem rootItem = Assert.Single(tree.Roots);
        Assert.Equal("Root", rootItem.Node.Name);

        SceneNodeTreeItem childItem = Assert.Single(rootItem.Children);
        Assert.Equal("A", childItem.Node.Name);

        SceneNodeTreeItem grandchildItem = Assert.Single(childItem.Children);
        Assert.Equal("B", grandchildItem.Node.Name);
        Assert.Empty(grandchildItem.Children);
    }

    [Fact]
    public void Build_ParentIdPointsToMissingNode_TreatsNodeAsRoot()
    {
        SceneNode orphan = MakeNode(StableId.New(), "Orphan", StableId.New());

        SceneNodeTree tree = SceneNodeTree.Build([orphan]);

        SceneNodeTreeItem rootItem = Assert.Single(tree.Roots);
        Assert.Equal("Orphan", rootItem.Node.Name);
    }

    [Fact]
    public void Build_MultipleRoots_AreAllReturned()
    {
        SceneNode rootA = MakeNode(StableId.New(), "RootA", null);
        SceneNode rootB = MakeNode(StableId.New(), "RootB", null);

        SceneNodeTree tree = SceneNodeTree.Build([rootA, rootB]);

        Assert.Equal(2, tree.Roots.Count);
        Assert.Contains(tree.Roots, item => item.Node.Name == "RootA");
        Assert.Contains(tree.Roots, item => item.Node.Name == "RootB");
    }

    [Fact]
    public void Build_MutualCycleWithNoValidRoot_Throws()
    {
        StableId idA = StableId.New();
        StableId idB = StableId.New();

        // A's parent is B and B's parent is A: both have a valid, existing parent, so neither
        // qualifies as a root and the pair is unreachable from any root traversal.
        SceneNode nodeA = MakeNode(idA, "A", idB);
        SceneNode nodeB = MakeNode(idB, "B", idA);

        Assert.Throws<InvalidOperationException>(() => SceneNodeTree.Build([nodeA, nodeB]));
    }
}
