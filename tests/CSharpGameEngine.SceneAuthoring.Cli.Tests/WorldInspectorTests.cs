namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// <see cref="WorldInspector"/>は、生成された<c>WorldSnapshot.Capture</c>が返すエントリ配列を
/// リフレクションで読む(ADR-0033)。対象アセンブリの型を一切コンパイル時に知らないという実運用と
/// 同じ条件を、<see cref="FakeSnapshotEntry"/>で再現して検証する。
/// </summary>
public class WorldInspectorTests {
    [Fact]
    public void Inspect_ReadsPositionRotationScale_ByConventionalTypeName(){
        FakeSnapshotEntry[]Snapshot=[
            FakeSnapshotEntry.Node(
                0,
                new Position { X=1f,Y=2f,Z=3f },
                new Rotation { X=0f,Y=0f,Z=0f,W=1f },
                new Scale { X=2f,Y=2f,Z=2f }),
        ];

        IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(Snapshot);

        ScenePreviewNode Node=Assert.Single(Nodes);
        Assert.Equal(0,Node.EntityId);
        Assert.Equal([1f,2f,3f],Node.Position);
        Assert.Equal([0f,0f,0f,1f],Node.Rotation);
        Assert.Equal([2f,2f,2f],Node.Scale);
        Assert.Contains("Position",Node.ComponentTypeNames);
        Assert.Contains("Rotation",Node.ComponentTypeNames);
        Assert.Contains("Scale",Node.ComponentTypeNames);
    }

    [Fact]
    public void Inspect_ReadsParentLink_FromTheSnapshotEntryItself(){
        // 親リンクは生成時点で解決済み(どのArchetypeがParentを持つかはGeneratorが知っている)なので、
        // ブリッジ側でParent型を型判定する必要はない。
        FakeSnapshotEntry[]Snapshot=[
            FakeSnapshotEntry.Node(0,new SceneNodeTag()),
            FakeSnapshotEntry.Child(1,0,new Position { X=5f }),
        ];

        IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(Snapshot);

        ScenePreviewNode RootNode=Assert.Single(Nodes,N=>N.EntityId==0);
        ScenePreviewNode ChildNode=Assert.Single(Nodes,N=>N.EntityId==1);
        Assert.Null(RootNode.ParentEntityId);
        Assert.Equal(0,ChildNode.ParentEntityId);
    }

    [Fact]
    public void Inspect_OrdersNodesByEntityIdAscending(){
        FakeSnapshotEntry[]Snapshot=[
            FakeSnapshotEntry.Node(2,new SceneNodeTag()),
            FakeSnapshotEntry.Node(0,new SceneNodeTag()),
            FakeSnapshotEntry.Node(1,new SceneNodeTag()),
        ];

        IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(Snapshot);

        Assert.Equal([0,1,2],[.. Nodes.Select(N=>N.EntityId)]);
    }

    [Fact]
    public void Inspect_UnrecognizedComponentType_IsRecordedByNameOnly_TransformStaysDefault(){
        FakeSnapshotEntry[]Snapshot=[FakeSnapshotEntry.Node(0,new Health { Value=100f })];

        IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(Snapshot);

        ScenePreviewNode Node=Assert.Single(Nodes);
        Assert.Contains("Health",Node.ComponentTypeNames);
        Assert.Equal([0f,0f,0f],Node.Position);
        Assert.Equal([0f,0f,0f,1f],Node.Rotation);
        Assert.Equal([1f,1f,1f],Node.Scale);
    }

    [Fact]
    public void Inspect_MultipleArchetypes_AreAllIncluded(){
        FakeSnapshotEntry[]Snapshot=[
            FakeSnapshotEntry.Node(0,new Position()),
            FakeSnapshotEntry.Node(1,new Position(),new Health { Value=1f }),
        ];

        IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(Snapshot);

        Assert.Equal(2,Nodes.Count);
        Assert.Contains(Nodes,N=>N.EntityId==0);
        Assert.Contains(Nodes,N=>N.EntityId==1);
    }

    [Fact]
    public void Inspect_EmptySnapshot_ReturnsNoNodes(){
        Assert.Empty(WorldInspector.Inspect(Array.Empty<FakeSnapshotEntry>()));
    }
}
