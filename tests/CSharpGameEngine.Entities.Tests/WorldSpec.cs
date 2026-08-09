namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// 閉集合Archetype方式のランタイム(ADR-0033)の基本動作。<c>World.Create</c>/<c>MoveArchetype</c>/
/// <c>PlaybackMoveArchetypes</c>はいずれもGeneratorがComponent構成ごとに生成したオーバーロードで、
/// 呼び出し側はArchetype名を一切書かない。
/// </summary>
public class WorldSpec
{
    [Fact]
    public void Create_AssignsSequentialEntityIds_AndAppendsToTheMatchingList()
    {
        World world = new();

        Entity first = world.Create(new Position { X = 1f });
        Entity second = world.Create(new Position { X = 2f });

        Assert.Equal(0, first.Id);
        Assert.Equal(1, second.Id);
        Assert.Equal(2, world.ListArchetype0.Count);
        Assert.Equal(1f, world.ListArchetype0[0].Position.X);
        Assert.Equal(2f, world.ListArchetype0[1].Position.X);
    }

    [Fact]
    public void Create_ResolvesTheArchetypeFromComponentTypesAlone()
    {
        World world = new();

        world.Create(new Position { X = 1f });
        world.Create(new Position { X = 2f }, new Velocity { X = 9f });

        Assert.Equal(1, world.ListArchetype0.Count);
        Assert.Equal(1, world.ListArchetype1.Count);
        Assert.Equal(9f, world.ListArchetype1[0].Velocity.X);
    }

    [Fact]
    public void MoveArchetype_IsDeferredUntilPlayback()
    {
        World world = new();
        State state = new(world);

        Entity entity = world.Create(new Position { X = 1f });

        world.MoveArchetype(entity, new Position { X = 5f }, new Velocity { X = 7f }, ref state);

        // Playback前は移動していない。
        Assert.Equal(1, world.ListArchetype0.Count);
        Assert.Equal(0, world.ListArchetype1.Count);

        world.PlaybackMoveArchetypes(ref state);

        Assert.Equal(0, world.ListArchetype0.Count);
        Assert.Equal(1, world.ListArchetype1.Count);
        Assert.Equal(5f, world.ListArchetype1[0].Position.X);
        Assert.Equal(7f, world.ListArchetype1[0].Velocity.X);
        Assert.Equal(entity, world.ListArchetype1[0].Entity);
    }

    [Fact]
    public void MoveArchetype_SwapBackRepairsTheMovedEntitysIndex()
    {
        World world = new();
        State state = new(world);

        Entity first = world.Create(new Position { X = 1f });
        Entity second = world.Create(new Position { X = 2f });
        Entity third = world.Create(new Position { X = 3f });

        // 先頭を移動させると、swap-backで末尾のthirdが先頭へ詰められる。
        world.MoveArchetype(first, new Position { X = 1f }, new Velocity(), ref state);
        world.PlaybackMoveArchetypes(ref state);

        Assert.Equal(2, world.ListArchetype0.Count);
        Assert.Equal(third, world.ListArchetype0[0].Entity);
        Assert.Equal(second, world.ListArchetype0[1].Entity);

        // 位置表が修復されていれば、詰められたthirdをさらに移動しても正しい行が消える。
        world.MoveArchetype(third, new Position { X = 3f }, new Velocity(), ref state);
        world.PlaybackMoveArchetypes(ref state);

        Assert.Equal(1, world.ListArchetype0.Count);
        Assert.Equal(second, world.ListArchetype0[0].Entity);
        Assert.Equal(2f, world.ListArchetype0[0].Position.X);
    }

    [Fact]
    public void PlaybackMoveArchetypes_ClearsTheCommandQueue()
    {
        World world = new();
        State state = new(world);

        Entity entity = world.Create(new Position());
        world.MoveArchetype(entity, new Position(), new Velocity(), ref state);
        world.PlaybackMoveArchetypes(ref state);

        // 2回目のPlaybackは何もしない(コマンドが残っていれば二重適用で例外になる)。
        world.PlaybackMoveArchetypes(ref state);

        Assert.Equal(1, world.ListArchetype1.Count);
    }

    [Fact]
    public void State_CarriesTheWorldAndEnabledFlag()
    {
        World world = new();
        State state = new(world) { Enabled = true };

        Assert.True(state.Enabled);

        state.Enabled = false;

        Assert.False(state.Enabled);
        Assert.Equal(0, state.World.ListArchetype0.Count);
    }
}
