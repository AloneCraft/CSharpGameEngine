namespace CSharpGameEngine.Ecs.Tests;

public sealed class AosEntityStoreSpec
{
    [Fact]
    public void Create_ReturnsUniqueIncrementingIds()
    {
        AosEntityStore<FakeHealth> store = new();

        Entity first = store.Create(new FakeHealth { Value = 1 });
        Entity second = store.Create(new FakeHealth { Value = 2 });

        Assert.NotEqual(first, second);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void GetRowRef_WriteThenRead_RoundTrips()
    {
        AosEntityStore<FakeHealth> store = new();
        Entity entity = store.Create(new FakeHealth { Value = 10 });

        store.GetRowRef(entity).Value = 42;

        Assert.Equal(42, store.GetRowRef(entity).Value);
    }

    [Fact]
    public void IsAlive_TrueAfterCreate_FalseAfterDestroy()
    {
        AosEntityStore<FakeHealth> store = new();
        Entity entity = store.Create(default);

        Assert.True(store.IsAlive(entity));

        store.Destroy(entity);

        Assert.False(store.IsAlive(entity));
    }

    [Fact]
    public void GetRowRef_OnDestroyedEntity_Throws()
    {
        AosEntityStore<FakeHealth> store = new();
        Entity entity = store.Create(default);
        store.Destroy(entity);

        Assert.Throws<InvalidOperationException>(() => store.GetRowRef(entity));
    }

    [Fact]
    public void Destroy_ThenCreate_ReusesIdWithIncrementedGeneration()
    {
        AosEntityStore<FakeHealth> store = new();
        Entity first = store.Create(default);

        store.Destroy(first);
        Entity second = store.Create(default);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Generation + 1, second.Generation);
    }

    [Fact]
    public void Destroy_MiddleEntity_SwapsBackAndOnlyMovesTheDisplacedEntitysLocation()
    {
        AosEntityStore<FakeHealth> store = new();
        Entity first = store.Create(new FakeHealth { Value = 1 });
        Entity middle = store.Create(new FakeHealth { Value = 2 });
        Entity last = store.Create(new FakeHealth { Value = 3 });

        store.Destroy(middle);

        Assert.True(store.IsAlive(first));
        Assert.False(store.IsAlive(middle));
        Assert.True(store.IsAlive(last));
        Assert.Equal(1, store.GetRowRef(first).Value);
        Assert.Equal(3, store.GetRowRef(last).Value);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void AsRowsSpan_StaysDenseAfterCreateAndDestroyChurn()
    {
        AosEntityStore<FakeHealth> store = new();
        Entity first = store.Create(new FakeHealth { Value = 1 });
        Entity second = store.Create(new FakeHealth { Value = 2 });
        Entity third = store.Create(new FakeHealth { Value = 3 });

        store.Destroy(first);

        Assert.Equal(2, store.Count);
        Assert.Equal(2, store.AsRowsSpan().Length);
        Assert.Equal(2, store.AsEntitiesSpan().Length);
        Assert.True(store.IsAlive(second));
        Assert.True(store.IsAlive(third));
        Assert.Equal(2, store.GetRowRef(second).Value);
        Assert.Equal(3, store.GetRowRef(third).Value);
    }

    [Fact]
    public void Create_BeyondInitialCapacity_GrowsWithoutCorruption()
    {
        AosEntityStore<FakeHealth> store = new();
        List<Entity> entities = [];

        for (int i = 0; i < 32; i++)
        {
            entities.Add(store.Create(new FakeHealth { Value = i }));
        }

        for (int i = 0; i < entities.Count; i++)
        {
            Assert.Equal(i, store.GetRowRef(entities[i]).Value);
        }
    }
}
