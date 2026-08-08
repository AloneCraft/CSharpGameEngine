namespace CSharpGameEngine.Entities.Tests;

public class EntityCommandBufferSpec
{
    [Fact]
    public void Playback_OnDeadEntity_SkipsWithoutThrowing()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);
        world.Destroy(entity);

        world.Commands.Add<Velocity>(entity);
        Exception? exception = Record.Exception(() => world.Commands.Playback(world));

        Assert.Null(exception);
    }

    [Fact]
    public void Playback_ClearsRecordedCommands_SoASecondEmptyPlaybackIsHarmless()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Add<Velocity>(entity);
        world.Commands.Playback(world);

        Exception? exception = Record.Exception(() => world.Commands.Playback(world));
        Assert.Null(exception);
    }

    [Fact]
    public void Destroy_QueuedThenPlayedBack_ActuallyDestroysEntity()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Destroy(entity);
        world.Commands.Playback(world);

        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void MultipleEntities_RecordedCommandsApplyIndependently()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity a = world.CreateEntity(archetype);
        Entity b = world.CreateEntity(archetype);

        world.Commands.Add<Velocity>(a);
        world.Commands.Destroy(b);
        world.Commands.Playback(world);

        Assert.True(world.Has<Velocity>(a));
        Assert.False(world.IsAlive(b));
    }

    [Fact]
    public void RecordedOrder_AddBeforeDestroy_DestroyWins()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<Position>();
        Entity entity = world.CreateEntity(archetype);

        world.Commands.Add<Velocity>(entity);
        world.Commands.Destroy(entity);
        world.Commands.Playback(world);

        Assert.False(world.IsAlive(entity));
    }
}
