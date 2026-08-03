namespace CSharpGameEngine.Ecs.Tests;

/// <summary>
/// ユーザー要件の核心: 正規化後のコンポーネント構成が一致する複数の名前付きアーキタイプは、
/// 実行時に同一の内部ArchetypeStorageを共有する別名ビューとして扱われ、コンパイルエラーにはならない。
/// </summary>
public sealed class ArchetypeStorageSharingSpec
{
    [Fact]
    public void TwoArchetypesWithSameNormalizedComponentSet_ShareSameStorage()
    {
        World world = new();

        world.Create<FakeMovingObjectA>();
        world.Create<FakeMovingObjectB>();

        Assert.Equal(1, world.DebugArchetypeStorageCount);
    }

    [Fact]
    public void TwoArchetypesWithSameNormalizedComponentSet_DoNotInterfereWithEachOthersValues()
    {
        World world = new();

        FakeMovingObjectA a = world.Create<FakeMovingObjectA>();
        FakeMovingObjectB b = world.Create<FakeMovingObjectB>();
        a.Position.X = 1;
        b.Position.X = 2;

        Assert.Equal(1, a.Position.X);
        Assert.Equal(2, b.Position.X);
    }

    [Fact]
    public void DifferentComponentSets_UseDistinctStorages()
    {
        World world = new();

        world.Create<FakeHealthOnly>();
        world.Create<FakeMovingObjectA>();

        Assert.Equal(2, world.DebugArchetypeStorageCount);
    }
}
