namespace CSharpGameEngine.Entities.Tests;

public class EntityQuerySpec
{
    [Fact]
    public void Update_MatchesArchetypeContainingAllRequiredComponents()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position, Velocity>();
        world.CreateEntity(archetype);
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id, ComponentType<Velocity>.Id],
            none: [],
            access: [ComponentType<Position>.Id, ComponentType<Velocity>.Id]);

        QueryMatches matches = query.Update(world);

        Assert.Equal(1, matches.Count);
    }

    [Fact]
    public void Update_ExcludesArchetypesMissingARequiredComponent()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        world.CreateEntity(archetype);
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id, ComponentType<Velocity>.Id],
            none: [],
            access: [ComponentType<Position>.Id, ComponentType<Velocity>.Id]);

        QueryMatches matches = query.Update(world);

        Assert.Equal(0, matches.Count);
    }

    [Fact]
    public void Update_ExcludesArchetypesMatchingNone()
    {
        World world = new();
        EntityArchetype withTag = world.CreateArchetype<Position, DisabledTag>();
        world.CreateEntity(withTag);
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id],
            none: [ComponentType<DisabledTag>.Id],
            access: [ComponentType<Position>.Id]);

        QueryMatches matches = query.Update(world);

        Assert.Equal(0, matches.Count);
    }

    [Fact]
    public void Update_IncludesArchetypeNotMatchingNone()
    {
        World world = new();
        EntityArchetype withoutTag = world.CreateArchetype<Position>();
        world.CreateEntity(withoutTag);
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id],
            none: [ComponentType<DisabledTag>.Id],
            access: [ComponentType<Position>.Id]);

        QueryMatches matches = query.Update(world);

        Assert.Equal(1, matches.Count);
    }

    [Fact]
    public void Update_MatchesArchetypeCreatedAfterQueryConstruction()
    {
        World world = new();
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id], none: [], access: [ComponentType<Position>.Id]);
        Assert.Equal(0, query.Update(world).Count);

        EntityArchetype archetype = world.CreateArchetype<Position>();
        world.CreateEntity(archetype);

        Assert.Equal(1, query.Update(world).Count);
    }

    [Fact]
    public void ColumnAt_ResolvesToCorrectColumnRegardlessOfArchetypeDeclarationOrder()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Velocity, Position>();
        Entity entity = world.CreateEntity(archetype);
        world.GetRef<Position>(entity) = new Position { X = 99f };
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id], none: [], access: [ComponentType<Position>.Id]);

        QueryMatches matches = query.Update(world);
        Archetype matched = matches.ArchetypeAt(0);
        int column = matches.ColumnAt(0, 0);

        Assert.Equal(99f, matched.GetReadOnlySpan<Position>(column)[0].X);
    }

    [Fact]
    public void Update_CalledTwiceWithNoNewArchetypes_ReturnsSameMatchCount()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        world.CreateEntity(archetype);
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id], none: [], access: [ComponentType<Position>.Id]);

        int firstCount = query.Update(world).Count;
        int secondCount = query.Update(world).Count;

        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public void Update_TwoDistinctArchetypesBothMatching_AreBothReturned()
    {
        World world = new();
        world.CreateEntity(world.CreateArchetype<Position>());
        world.CreateEntity(world.CreateArchetype<Position, Velocity>());
        EntityQuery query = EntityQuery.Create(
            all: [ComponentType<Position>.Id], none: [], access: [ComponentType<Position>.Id]);

        QueryMatches matches = query.Update(world);

        Assert.Equal(2, matches.Count);
    }
}
