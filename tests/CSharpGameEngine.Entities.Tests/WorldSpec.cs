namespace CSharpGameEngine.Entities.Tests;

public class WorldSpec
{
    [Fact]
    public void CreateEntity_IsAlive()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();

        Entity entity = world.CreateEntity(archetype);

        Assert.True(world.IsAlive(entity));
    }

    [Fact]
    public void Destroy_MakesEntityNotAlive()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Destroy(entity);

        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void Destroy_ThenCreate_ReusesIdWithIncrementedGeneration()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity first = world.CreateEntity(archetype);
        world.Destroy(first);

        Entity second = world.CreateEntity(archetype);

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.Generation, second.Generation);
    }

    [Fact]
    public void StaleHandle_IsNotAlive_AfterIdReuse()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity first = world.CreateEntity(archetype);
        world.Destroy(first);
        world.CreateEntity(archetype);

        Assert.False(world.IsAlive(first));
    }

    [Fact]
    public void GetRef_ThenWrite_IsObservedOnSubsequentRead()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.GetRef<Position>(entity) = new Position { X = 5f };

        Assert.Equal(5f, world.GetRef<Position>(entity).X);
    }

    [Fact]
    public void Has_DistinguishesPresentFromAbsentComponents()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        Assert.False(world.Has<Velocity>(entity));
        Assert.True(world.Has<Position>(entity));
    }

    [Fact]
    public void CreateArchetype_SameComponentSetInDifferentDeclarationOrder_ResolvesToSameArchetype()
    {
        World world = new();
        EntityArchetype declaredAsPositionVelocity = world.CreateArchetype<Position, Velocity>();
        EntityArchetype declaredAsVelocityPosition = world.CreateArchetype<Velocity, Position>();

        Assert.Equal(1, world.ArchetypeCount);
        Assert.Equal(declaredAsPositionVelocity.Archetype.Index, declaredAsVelocityPosition.Archetype.Index);
    }

    [Fact]
    public void DestroyingMiddleEntity_RepairsSwappedEntitysData()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity a = world.CreateEntity(archetype);
        Entity b = world.CreateEntity(archetype);
        Entity c = world.CreateEntity(archetype);
        world.GetRef<Position>(a) = new Position { X = 1f };
        world.GetRef<Position>(b) = new Position { X = 2f };
        world.GetRef<Position>(c) = new Position { X = 3f };

        world.Destroy(a);

        Assert.Equal(3f, world.GetRef<Position>(c).X);
        Assert.Equal(2f, world.GetRef<Position>(b).X);
    }

    [Fact]
    public void GetRef_ForMissingComponent_Throws()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        Assert.Throws<InvalidOperationException>(() => world.GetRef<Velocity>(entity));
    }

    [Fact]
    public void OperationOnDeadEntity_Throws()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);
        world.Destroy(entity);

        Assert.Throws<InvalidOperationException>(() => world.GetRef<Position>(entity));
    }

    [Fact]
    public void CreateArchetype_WithEightComponents_Succeeds()
    {
        World world = new();

        EntityArchetype archetype = world.CreateArchetype<Position, Velocity, Health, DisabledTag, Position2, Position3, Position4, Position5>();
        Entity entity = world.CreateEntity(archetype);

        Assert.True(world.IsAlive(entity));
    }

    public struct Position2 { public float Value; }
    public struct Position3 { public float Value; }
    public struct Position4 { public float Value; }
    public struct Position5 { public float Value; }
}
