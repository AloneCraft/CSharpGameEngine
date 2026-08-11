namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// 閉集合Archetype方式のランタイム(ADR-0033)の基本動作。<c>World.Create</c>/<c>MoveArchetype</c>/
/// <c>PlaybackStructuralChanges</c>はいずれもGeneratorがComponent構成ごとに生成したオーバーロードで、
/// 呼び出し側はArchetype名を一切書かない。
/// </summary>
public class WorldSpec {
    [Fact]
    public void Create_AssignsSequentialEntityIds_AndAppendsToTheMatchingList(){
        World world=new();

        Entity First=world.Create(new Position { X=1f });
        Entity Second=world.Create(new Position { X=2f });

        Assert.Equal(0,First.Id);
        Assert.Equal(1,Second.Id);
        Assert.Equal(2,world.Archetype0.Count);
        Assert.Equal(1f,world.Archetype0.GetPositionRefUnchecked(0).X);
        Assert.Equal(2f,world.Archetype0.GetPositionRefUnchecked(1).X);
    }

    [Fact]
    public void Create_ResolvesTheArchetypeFromComponentTypesAlone(){
        World world=new();

        world.Create(new Position { X=1f });
        world.Create(new Position { X=2f },new Velocity { X=9f });

        Assert.Equal(1,world.Archetype0.Count);
        Assert.Equal(1,world.Archetype1.Count);
        Assert.Equal(9f,world.Archetype1.GetVelocityRefUnchecked(0).X);
    }

    [Fact]
    public void MoveArchetype_IsDeferredUntilPlayback(){
        World world=new();
        State state=new(world);

        Entity entity=world.Create(new Position { X=1f });

        world.MoveArchetype(entity,new Position { X=5f },new Velocity { X=7f },ref state);

        // Playback前は移動していない。
        Assert.Equal(1,world.Archetype0.Count);
        Assert.Equal(0,world.Archetype1.Count);

        world.PlaybackStructuralChanges(ref state);

        Assert.Equal(0,world.Archetype0.Count);
        Assert.Equal(1,world.Archetype1.Count);
        Assert.Equal(5f,world.Archetype1.GetPositionRefUnchecked(0).X);
        Assert.Equal(7f,world.Archetype1.GetVelocityRefUnchecked(0).X);
        Assert.Equal(entity,world.Archetype1.GetEntityRefUnchecked(0));
    }

    [Fact]
    public void MoveArchetype_SwapBackRepairsTheMovedEntitysIndex(){
        World world=new();
        State state=new(world);

        Entity First=world.Create(new Position { X=1f });
        Entity Second=world.Create(new Position { X=2f });
        Entity Third=world.Create(new Position { X=3f });

        // 先頭を移動させると、swap-backで末尾のthirdが先頭へ詰められる。
        world.MoveArchetype(First,new Position { X=1f },new Velocity(),ref state);
        world.PlaybackStructuralChanges(ref state);

        Assert.Equal(2,world.Archetype0.Count);
        Assert.Equal(Third,world.Archetype0.GetEntityRefUnchecked(0));
        Assert.Equal(Second,world.Archetype0.GetEntityRefUnchecked(1));

        // 位置表が修復されていれば、詰められたthirdをさらに移動しても正しい行が消える。
        world.MoveArchetype(Third,new Position { X=3f },new Velocity(),ref state);
        world.PlaybackStructuralChanges(ref state);

        Assert.Equal(1,world.Archetype0.Count);
        Assert.Equal(Second,world.Archetype0.GetEntityRefUnchecked(0));
        Assert.Equal(2f,world.Archetype0.GetPositionRefUnchecked(0).X);
    }

    [Fact]
    public void PlaybackStructuralChanges_ClearsTheCommandQueue(){
        World world=new();
        State state=new(world);

        Entity entity=world.Create(new Position());
        world.MoveArchetype(entity,new Position(),new Velocity(),ref state);
        world.PlaybackStructuralChanges(ref state);

        // 2回目のPlaybackは何もしない(コマンドが残っていれば二重適用で例外になる)。
        world.PlaybackStructuralChanges(ref state);

        Assert.Equal(1,world.Archetype1.Count);
    }

    [Fact]
    public void State_CarriesTheWorldAndEnabledFlag(){
        World world=new();
        State state=new(world){ Enabled=true };

        Assert.True(state.Enabled);

        state.Enabled=false;

        Assert.False(state.Enabled);
        Assert.Equal(0,state.World.Archetype0.Count);
    }
}

