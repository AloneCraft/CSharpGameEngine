namespace CSharpGameEngine.Ecs.Tests;

public sealed class ArchetypeChunkSpec
{
    [Fact]
    public void TryGetArchetypeChunk_NoEntityOfThatArchetypeYet_ReturnsFalse()
    {
        World world = new();

        bool found = world.TryGetArchetypeChunk<FakeHealthOnly>(out ArchetypeChunk chunk);

        Assert.False(found);
        Assert.Equal(0, chunk.Count);
        Assert.True(chunk.Entities.IsEmpty);
    }

    [Fact]
    public void TryGetArchetypeChunk_AfterCreate_ReflectsCountAndEntities()
    {
        World world = new();
        FakeHealthOnly first = world.Create<FakeHealthOnly>();
        FakeHealthOnly second = world.Create<FakeHealthOnly>();

        bool found = world.TryGetArchetypeChunk<FakeHealthOnly>(out ArchetypeChunk chunk);

        Assert.True(found);
        Assert.Equal(2, chunk.Count);
        Assert.Equal([first.Entity, second.Entity], chunk.Entities.ToArray());
    }

    [Fact]
    public void GetColumnSpan_ReflectsDirectGetComponentRefWrites()
    {
        World world = new();
        FakeHealthOnly entity = world.Create<FakeHealthOnly>();
        entity.Health.Value = 42;

        world.TryGetArchetypeChunk<FakeHealthOnly>(out ArchetypeChunk chunk);
        Span<FakeHealth> healthColumn = chunk.GetColumnSpan<FakeHealth>(0);

        Assert.Equal(42, healthColumn[0].Value);

        healthColumn[0].Value = 99;

        Assert.Equal(99, entity.Health.Value);
    }

    [Fact]
    public void TryGetArchetypeChunk_TwoArchetypesWithSameNormalizedComponentSet_ShareTheSameChunk()
    {
        World world = new();
        FakeMovingObjectA a = world.Create<FakeMovingObjectA>();
        FakeMovingObjectB b = world.Create<FakeMovingObjectB>();
        a.Position.X = 1;
        b.Position.X = 2;

        world.TryGetArchetypeChunk<FakeMovingObjectA>(out ArchetypeChunk chunkFromA);
        world.TryGetArchetypeChunk<FakeMovingObjectB>(out ArchetypeChunk chunkFromB);

        // ArchetypeSignatureがType[]シーケンス一致だけで解決するため、正規化済み構成が同じ
        // 異なるアーキタイプ型は同一のArchetypeStorage(=同一Chunk)を指す。
        Assert.Equal(2, chunkFromA.Count);
        Assert.Equal(2, chunkFromB.Count);
        Assert.Equal(chunkFromA.Entities.ToArray(), chunkFromB.Entities.ToArray());
    }
}
