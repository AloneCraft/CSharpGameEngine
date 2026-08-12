namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// 閉集合Archetype方式のランタイム(ADR-0033)の基本動作。<c>World.Create</c>/<c>MoveArchetype</c>/
/// <c>PlaybackStructuralChanges</c>はいずれもGeneratorがComponent構成ごとに生成したオーバーロードで、
/// 呼び出し側はArchetype名を一切書かない。
/// </summary>
public class WorldSpec {
    [Fact]
    public void Create_AssignsSequentialEntityIds_AndAppendsToTheMatchingList(){
        World World=new();

        Entity First=World.Create(new Position { X=1f });
        Entity Second=World.Create(new Position { X=2f });

        Assert.Equal(0,First.Id);
        Assert.Equal(1,Second.Id);
        Assert.Equal(2,World.Archetype0.Count);
        Assert.Equal(1f,World.Archetype0.GetPositionRefUnchecked(0).X);
        Assert.Equal(2f,World.Archetype0.GetPositionRefUnchecked(1).X);
    }

    [Fact]
    public void Create_ResolvesTheArchetypeFromComponentTypesAlone(){
        World World=new();

        World.Create(new Position { X=1f });
        World.Create(new Position { X=2f },new Velocity { X=9f });

        Assert.Equal(1,World.Archetype0.Count);
        Assert.Equal(1,World.Archetype1.Count);
        Assert.Equal(9f,World.Archetype1.GetVelocityRefUnchecked(0).X);
    }

    [Fact]
    public void MoveArchetype_IsDeferredUntilPlayback(){
        World World=new();
        State State=new(World);

        Entity Entity=World.Create(new Position { X=1f });

        World.MoveArchetype(Entity,new Position { X=5f },new Velocity { X=7f },ref State);

        // Playback前は移動していない。
        Assert.Equal(1,World.Archetype0.Count);
        Assert.Equal(0,World.Archetype1.Count);

        World.PlaybackStructuralChanges(ref State);

        Assert.Equal(0,World.Archetype0.Count);
        Assert.Equal(1,World.Archetype1.Count);
        Assert.Equal(5f,World.Archetype1.GetPositionRefUnchecked(0).X);
        Assert.Equal(7f,World.Archetype1.GetVelocityRefUnchecked(0).X);
        Assert.Equal(Entity,World.Archetype1.GetEntityRefUnchecked(0));
    }

    [Fact]
    public void MoveArchetype_SwapBackRepairsTheMovedEntitysIndex(){
        World World=new();
        State State=new(World);

        Entity First=World.Create(new Position { X=1f });
        Entity Second=World.Create(new Position { X=2f });
        Entity Third=World.Create(new Position { X=3f });

        // 先頭を移動させると、swap-backで末尾のthirdが先頭へ詰められる。
        World.MoveArchetype(First,new Position { X=1f },new Velocity(),ref State);
        World.PlaybackStructuralChanges(ref State);

        Assert.Equal(2,World.Archetype0.Count);
        Assert.Equal(Third,World.Archetype0.GetEntityRefUnchecked(0));
        Assert.Equal(Second,World.Archetype0.GetEntityRefUnchecked(1));

        // 位置表が修復されていれば、詰められたthirdをさらに移動しても正しい行が消える。
        World.MoveArchetype(Third,new Position { X=3f },new Velocity(),ref State);
        World.PlaybackStructuralChanges(ref State);

        Assert.Equal(1,World.Archetype0.Count);
        Assert.Equal(Second,World.Archetype0.GetEntityRefUnchecked(0));
        Assert.Equal(2f,World.Archetype0.GetPositionRefUnchecked(0).X);
    }

    [Fact]
    public void PlaybackStructuralChanges_ClearsTheCommandQueue(){
        World World=new();
        State State=new(World);

        Entity Entity=World.Create(new Position());
        World.MoveArchetype(Entity,new Position(),new Velocity(),ref State);
        World.PlaybackStructuralChanges(ref State);

        // 2回目のPlaybackは何もしない(コマンドが残っていれば二重適用で例外になる)。
        World.PlaybackStructuralChanges(ref State);

        Assert.Equal(1,World.Archetype1.Count);
    }

    [Fact]
    public void State_CarriesTheWorldAndEnabledFlag(){
        World World=new();
        State State=new(World){ Enabled=true };

        Assert.True(State.Enabled);

        State.Enabled=false;

        Assert.False(State.Enabled);
        Assert.Equal(0,State.World.Archetype0.Count);
    }
}

