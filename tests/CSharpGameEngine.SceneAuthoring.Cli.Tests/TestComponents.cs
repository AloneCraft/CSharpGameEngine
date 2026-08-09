namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

public struct Position
{
    public float X;
    public float Y;
    public float Z;
}

public struct Rotation
{
    public float X;
    public float Y;
    public float Z;
    public float W;
}

public struct Scale
{
    public float X;
    public float Y;
    public float Z;
}

public struct Health
{
    public float Value;
}

public struct SceneNodeTag
{
}

/// <summary>
/// 生成される<c>CSharpGameEngine.Entities.ArchetypeSnapshotEntry</c>と同じ公開フィールド構成を持つ
/// テスト用のスタンドイン。<see cref="WorldInspector"/>はスナップショットをリフレクションで
/// (フィールド名で)読むため、対象アセンブリを実際にビルドしなくてもこの型で挙動を検証できる
/// - これは実運用と同じ経路でもある(ADR-0033: ブリッジは対象の型を一切コンパイル時に知らない)。
/// </summary>
public struct FakeSnapshotEntry
{
    public int EntityId;
    public int ParentEntityId;
    public bool HasParent;
    public string ArchetypeName;
    public object[] ComponentValues;

    public static FakeSnapshotEntry Node(int entityId, params object[] componentValues) => new()
    {
        EntityId = entityId,
        ParentEntityId = -1,
        HasParent = false,
        ArchetypeName = "Archetype0",
        ComponentValues = componentValues,
    };

    public static FakeSnapshotEntry Child(int entityId, int parentEntityId, params object[] componentValues) => new()
    {
        EntityId = entityId,
        ParentEntityId = parentEntityId,
        HasParent = true,
        ArchetypeName = "Archetype1",
        ComponentValues = componentValues,
    };
}
