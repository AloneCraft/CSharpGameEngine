namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// <see cref="WorldInspector"/>は、生成された<c>WorldSnapshot.Capture</c>が返すエントリ配列を
/// リフレクションで読む(ADR-0033)。対象アセンブリの型を一切コンパイル時に知らないという実運用と
/// 同じ条件を、<see cref="FakeSnapshotEntry"/>で再現して検証する。
/// </summary>
public class WorldInspectorTests
{
    [Fact]
    public void Inspect_ReadsPositionRotationScale_ByConventionalTypeName()
    {
        FakeSnapshotEntry[] snapshot =
        [
            FakeSnapshotEntry.Node(
                0,
                new Position { X = 1f, Y = 2f, Z = 3f },
                new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f },
                new Scale { X = 2f, Y = 2f, Z = 2f }),
        ];

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(snapshot);

        ScenePreviewNode node = Assert.Single(nodes);
        Assert.Equal(0, node.EntityId);
        Assert.Equal([1f, 2f, 3f], node.Position);
        Assert.Equal([0f, 0f, 0f, 1f], node.Rotation);
        Assert.Equal([2f, 2f, 2f], node.Scale);
        Assert.Contains("Position", node.ComponentTypeNames);
        Assert.Contains("Rotation", node.ComponentTypeNames);
        Assert.Contains("Scale", node.ComponentTypeNames);
    }

    [Fact]
    public void Inspect_ReadsParentLink_FromTheSnapshotEntryItself()
    {
        // 親リンクは生成時点で解決済み(どのArchetypeがParentを持つかはGeneratorが知っている)なので、
        // ブリッジ側でParent型を型判定する必要はない。
        FakeSnapshotEntry[] snapshot =
        [
            FakeSnapshotEntry.Node(0, new SceneNodeTag()),
            FakeSnapshotEntry.Child(1, 0, new Position { X = 5f }),
        ];

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(snapshot);

        ScenePreviewNode rootNode = Assert.Single(nodes, n => n.EntityId == 0);
        ScenePreviewNode childNode = Assert.Single(nodes, n => n.EntityId == 1);
        Assert.Null(rootNode.ParentEntityId);
        Assert.Equal(0, childNode.ParentEntityId);
    }

    [Fact]
    public void Inspect_OrdersNodesByEntityIdAscending()
    {
        FakeSnapshotEntry[] snapshot =
        [
            FakeSnapshotEntry.Node(2, new SceneNodeTag()),
            FakeSnapshotEntry.Node(0, new SceneNodeTag()),
            FakeSnapshotEntry.Node(1, new SceneNodeTag()),
        ];

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(snapshot);

        Assert.Equal([0, 1, 2], [.. nodes.Select(n => n.EntityId)]);
    }

    [Fact]
    public void Inspect_UnrecognizedComponentType_IsRecordedByNameOnly_TransformStaysDefault()
    {
        FakeSnapshotEntry[] snapshot = [FakeSnapshotEntry.Node(0, new Health { Value = 100f })];

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(snapshot);

        ScenePreviewNode node = Assert.Single(nodes);
        Assert.Contains("Health", node.ComponentTypeNames);
        Assert.Equal([0f, 0f, 0f], node.Position);
        Assert.Equal([0f, 0f, 0f, 1f], node.Rotation);
        Assert.Equal([1f, 1f, 1f], node.Scale);
    }

    [Fact]
    public void Inspect_MultipleArchetypes_AreAllIncluded()
    {
        FakeSnapshotEntry[] snapshot =
        [
            FakeSnapshotEntry.Node(0, new Position()),
            FakeSnapshotEntry.Node(1, new Position(), new Health { Value = 1f }),
        ];

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(snapshot);

        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.EntityId == 0);
        Assert.Contains(nodes, n => n.EntityId == 1);
    }

    [Fact]
    public void Inspect_EmptySnapshot_ReturnsNoNodes()
    {
        Assert.Empty(WorldInspector.Inspect(Array.Empty<FakeSnapshotEntry>()));
    }
}
