namespace CSharpGameEngine.Ecs.Tests;

public sealed class WorldSpec
{
    [Fact]
    public void CreateEntityCore_ReturnsUniqueIncrementingIds()
    {
        World world = new();

        FakeHealthOnly first = world.Create<FakeHealthOnly>();
        FakeHealthOnly second = world.Create<FakeHealthOnly>();

        Assert.NotEqual(first.Entity, second.Entity);
        Assert.Equal(0, first.Entity.Id);
        Assert.Equal(1, second.Entity.Id);
    }

    [Fact]
    public void GetComponentRef_WriteThenRead_RoundTrips()
    {
        World world = new();
        FakeHealthOnly entity = world.Create<FakeHealthOnly>();

        entity.Health.Value = 100;

        Assert.Equal(100, entity.Health.Value);
    }

    [Fact]
    public void IsAlive_TrueAfterCreate_FalseAfterDestroy()
    {
        World world = new();
        FakeHealthOnly entity = world.Create<FakeHealthOnly>();

        Assert.True(world.IsAlive(entity.Entity));

        world.Destroy(entity.Entity);

        Assert.False(world.IsAlive(entity.Entity));
    }

    [Fact]
    public void GetComponentRef_OnDestroyedEntity_Throws()
    {
        World world = new();
        FakeHealthOnly entity = world.Create<FakeHealthOnly>();
        world.Destroy(entity.Entity);

        Assert.Throws<InvalidOperationException>(() => world.GetComponentRef<FakeHealth>(entity.Entity, 0));
    }

    [Fact]
    public void Destroy_ThenCreate_ReusesIdWithIncrementedGeneration()
    {
        World world = new();
        FakeHealthOnly first = world.Create<FakeHealthOnly>();
        int firstId = first.Entity.Id;
        int firstGeneration = first.Entity.Generation;

        world.Destroy(first.Entity);
        FakeHealthOnly second = world.Create<FakeHealthOnly>();

        Assert.Equal(firstId, second.Entity.Id);
        Assert.Equal(firstGeneration + 1, second.Entity.Generation);
    }

    [Fact]
    public void Destroy_MiddleEntity_SwapsBackAndKeepsRemainingEntitiesCorrect()
    {
        World world = new();
        FakeHealthOnly first = world.Create<FakeHealthOnly>();
        FakeHealthOnly middle = world.Create<FakeHealthOnly>();
        FakeHealthOnly last = world.Create<FakeHealthOnly>();
        first.Health.Value = 1;
        middle.Health.Value = 2;
        last.Health.Value = 3;

        world.Destroy(middle.Entity);

        Assert.True(world.IsAlive(first.Entity));
        Assert.False(world.IsAlive(middle.Entity));
        Assert.True(world.IsAlive(last.Entity));
        Assert.Equal(1, first.Health.Value);
        Assert.Equal(3, last.Health.Value);
    }

    [Fact]
    public void CreateEntityCore_BeyondInitialCapacity_GrowsWithoutCorruption()
    {
        World world = new();
        List<FakeHealthOnly> entities = [];

        for (int i = 0; i < 32; i++)
        {
            FakeHealthOnly entity = world.Create<FakeHealthOnly>();
            entity.Health.Value = i;
            entities.Add(entity);
        }

        for (int i = 0; i < entities.Count; i++)
        {
            Assert.Equal(i, entities[i].Health.Value);
        }
    }

    [Fact]
    public void CollectViews_NoEntitiesOfThatArchetype_ReturnsEmptyWithoutAllocatingStorage()
    {
        World world = new();
        List<FakeHealthOnly> results = [];

        world.CollectViews(results);

        Assert.Empty(results);
        Assert.Equal(0, world.DebugArchetypeStorageCount);
    }

    [Fact]
    public void CollectViews_ReturnsOneViewPerLiveEntityInCreationOrder()
    {
        World world = new();
        FakeHealthOnly first = world.Create<FakeHealthOnly>();
        FakeHealthOnly second = world.Create<FakeHealthOnly>();
        FakeHealthOnly third = world.Create<FakeHealthOnly>();
        first.Health.Value = 10;
        second.Health.Value = 20;
        third.Health.Value = 30;

        List<FakeHealthOnly> results = [];
        world.CollectViews(results);

        Assert.Equal(3, results.Count);
        Assert.Equal([10, 20, 30], results.ConvertAll(r => r.Health.Value));
    }

    [Fact]
    public void CollectViews_AfterDestroyingMiddleEntity_ReflectsSwapBackNotStaleRows()
    {
        World world = new();
        FakeHealthOnly first = world.Create<FakeHealthOnly>();
        FakeHealthOnly middle = world.Create<FakeHealthOnly>();
        FakeHealthOnly last = world.Create<FakeHealthOnly>();
        first.Health.Value = 1;
        middle.Health.Value = 2;
        last.Health.Value = 3;

        world.Destroy(middle.Entity);

        List<FakeHealthOnly> results = [];
        world.CollectViews(results);

        Assert.Equal(2, results.Count);
        Assert.Equal([1, 3], results.ConvertAll(r => r.Health.Value));
    }

    [Fact]
    public void CollectViews_OnlyMatchesTheExactArchetypeNotOthersSharingAComponent()
    {
        World world = new();
        world.Create<FakeHealthOnly>().Health.Value = 42;
        world.Create<FakeMovingObjectA>();

        List<FakeHealthOnly> results = [];
        world.CollectViews(results);

        Assert.Single(results);
        Assert.Equal(42, results[0].Health.Value);
    }

    [Fact]
    public void CollectViews_AppendsToExistingListRatherThanReplacingIt()
    {
        World world = new();
        world.Create<FakeHealthOnly>().Health.Value = 1;

        List<FakeHealthOnly> results = [];
        world.CollectViews(results);
        Assert.Single(results);

        world.Create<FakeHealthOnly>().Health.Value = 2;
        world.CollectViews(results);

        // The second call appends views for *both* currently-alive entities on top of the first
        // call's result rather than clearing it first: [entity1] + [entity1, entity2] = 3 entries.
        Assert.Equal([1, 1, 2], results.ConvertAll(r => r.Health.Value));
    }
}
