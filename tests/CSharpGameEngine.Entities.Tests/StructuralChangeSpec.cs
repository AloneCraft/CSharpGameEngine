namespace CSharpGameEngine.Entities.Tests;

public class StructuralChangeSpec
{
    [Fact]
    public void AddComponent_ViaCommandBuffer_MovesEntityToNewArchetypeWithSharedColumnsPreserved()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);
        world.GetRef<Position>(entity) = new Position { X = 42f };

        world.Commands.Add<Velocity>(entity);
        world.Commands.Playback(world);

        Assert.True(world.Has<Position>(entity));
        Assert.True(world.Has<Velocity>(entity));
        Assert.Equal(42f, world.GetRef<Position>(entity).X);
    }

    [Fact]
    public void AddComponent_WithValue_NewColumnHoldsTheProvidedValue()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Add(entity, new Velocity { X = 9f });
        world.Commands.Playback(world);

        Assert.Equal(9f, world.GetRef<Velocity>(entity).X);
    }

    [Fact]
    public void AddComponent_WithoutValue_NewColumnDefaultsToZero()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Add<Velocity>(entity);
        world.Commands.Playback(world);

        Assert.Equal(0f, world.GetRef<Velocity>(entity).X);
    }

    [Fact]
    public void RemoveComponent_DropsOnlyThatColumn()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position, Velocity>();
        Entity entity = world.CreateEntity(archetype);
        world.GetRef<Position>(entity) = new Position { X = 7f };

        world.Commands.Remove<Velocity>(entity);
        world.Commands.Playback(world);

        Assert.True(world.Has<Position>(entity));
        Assert.False(world.Has<Velocity>(entity));
        Assert.Equal(7f, world.GetRef<Position>(entity).X);
    }

    [Fact]
    public void RemoveComponent_NotPresent_IsNoOp()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);
        int archetypeCountBefore = world.ArchetypeCount;

        world.Commands.Remove<Velocity>(entity);
        world.Commands.Playback(world);

        Assert.True(world.Has<Position>(entity));
        Assert.Equal(archetypeCountBefore, world.ArchetypeCount);
    }

    [Fact]
    public void AddThenRemoveSameComponentInOneFrame_NetsToRemoved()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Add<Health>(entity);
        world.Commands.Remove<Health>(entity);
        world.Commands.Playback(world);

        Assert.False(world.Has<Health>(entity));
    }

    [Fact]
    public void AddSameComponentTwiceInOneFrame_LastValueWins()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Add(entity, new Health { Value = 1f });
        world.Commands.Add(entity, new Health { Value = 2f });
        world.Commands.Playback(world);

        Assert.Equal(2f, world.GetRef<Health>(entity).Value);
    }

    [Fact]
    public void SwapBack_DuringStructuralMove_RepairsOtherEntitysRow()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity a = world.CreateEntity(archetype);
        Entity b = world.CreateEntity(archetype);
        Entity c = world.CreateEntity(archetype);
        world.GetRef<Position>(a) = new Position { X = 1f };
        world.GetRef<Position>(b) = new Position { X = 2f };
        world.GetRef<Position>(c) = new Position { X = 3f };

        world.Commands.Add<Velocity>(a);
        world.Commands.Playback(world);

        Assert.Equal(3f, world.GetRef<Position>(c).X);
        Assert.Equal(2f, world.GetRef<Position>(b).X);
    }

    [Fact]
    public void MoveToSameArchetype_IsNoOpAndKeepsExistingValue()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);
        world.GetRef<Position>(entity) = new Position { X = 3f };

        world.Commands.Add(entity, new Position { X = 3f });
        world.Commands.Playback(world);

        Assert.Equal(1, world.ArchetypeCount);
        Assert.Equal(3f, world.GetRef<Position>(entity).X);
    }

    [Fact]
    public void CreateEntity_ViaCommandBuffer_CanBeConfiguredInSameBuffer()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position, Health>();

        Entity temporary = world.Commands.CreateEntity(archetype);
        world.Commands.Add(temporary, new Position { X = 11f });
        world.Commands.Playback(world);

        Archetype created = world.GetArchetype(archetype.Archetype.Index);
        Assert.Equal(1, created.Count);
        int positionColumn = created.IndexOfColumn(ComponentType<Position>.Id);
        Assert.Equal(11f, created.GetReadOnlySpan<Position>(positionColumn)[0].X);
    }
}
