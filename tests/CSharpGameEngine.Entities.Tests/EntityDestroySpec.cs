namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// Entity破棄と世代付きslot再利用。
///
/// <para>
/// 配列は2種類あり、混同してはならない。<b>位置表</b>(<c>World.EntityArchetypeIndices</c>)は行番号が
/// そのまま<c>Entity.Id</c>で、追記のみでswap-backしない。<b>ArchetypeのSoA列</b>は行番号が<c>Index</c>で、
/// swap-backはこちらだけで起きる。破棄した位置表slotは再利用されるがGenerationが必ず進むため、
/// 古いハンドルが<b>別のEntityに化けることは構造的に起こらない</b>。
/// </para>
/// </summary>
public sealed class EntityDestroySpec {
    [Fact]
    public void CreatedEntityIsAlive(){
        World World=new();

        var Entity=World.Create(new Position { X=1f });

        Assert.True(World.IsAlive(Entity));
    }

    [Fact]
    public void DestroyedEntityIsNoLongerAlive(){
        World World=new();
        var Entity=World.Create(new Position { X=1f });

        World.Destroy(Entity);

        Assert.False(World.IsAlive(Entity));
    }

    [Fact]
    public void DestroyIsIdempotent(){
        World World=new();
        var Entity=World.Create(new Position());

        World.Destroy(Entity);
        World.Destroy(Entity);

        Assert.False(World.IsAlive(Entity));
    }

    [Fact]
    public void NeverSeenEntityIsNotAlive(){
        World World=new();

        Assert.False(World.IsAlive(new Entity(0)));
        Assert.False(World.IsAlive(new Entity(999)));
        Assert.False(World.IsAlive(new Entity(-1)));
    }

    [Fact]
    public void DestroyedSlotIsReusedWithANewGeneration(){
        World World=new();
        var First=World.Create(new Position { X=1f });
        World.Destroy(First);

        var Second=World.Create(new Position { X=2f });

        Assert.Equal(First.Index,Second.Index);
        Assert.NotEqual(First.Generation,Second.Generation);
        Assert.False(World.IsAlive(First));
        Assert.True(World.IsAlive(Second));
    }
    [Fact]
    public void StaleGenerationCannotReadMoveOrDestroyTheReplacement(){
        World World=new();
        State State=new(World){ Enabled=true };
        var Stale=World.Create(new Position { X=1f });
        World.Destroy(Stale);
        var Current=World.Create(new Position { X=2f });
        Assert.False(World.TryGetComponent(Stale,out Position _));
        Assert.False(World.MoveArchetype(Stale,new Position { X=9f },new Velocity(),ref State));
        Assert.False(World.DestroyDeferred(Stale,ref State));
        World.Destroy(Stale);
        Assert.True(World.IsAlive(Current));
        Assert.True(World.TryGetComponent(Current,out Position Position));
        Assert.Equal(2f,Position.X);
    }
    [Fact]
    public void DefaultEntityIsNeverAlive(){
        World World=new();
        var First=World.Create(new Position());
        Assert.False(World.IsAlive(default));
        Assert.True(World.IsAlive(First));
        Assert.NotEqual(0u,First.Generation);
    }

    [Fact]
    public void SurvivingEntityKeepsItsIdentityAfterASwapBack(){
        // 末尾のEntityが消えた行へ座り直すが、Entity.Idは動かない。動くのはArchetype内の座席番号だけ。
        World World=new();
        var A=World.Create(new Position { X=1f });
        var B=World.Create(new Position { X=2f });
        var C=World.Create(new Position { X=3f });

        World.Destroy(B);

        Assert.True(World.IsAlive(A));
        Assert.False(World.IsAlive(B));
        Assert.True(World.IsAlive(C));
        Assert.Equal(3f,World.Archetype0.GetPositionRefUnchecked(World.Archetype0.Count-1).X);
        Assert.Equal(2,World.Archetype0.Count);
    }

    [Fact]
    public void DestroyingTheLastEntityDoesNotDisturbTheOthers(){
        World World=new();
        var A=World.Create(new Position { X=1f });
        var B=World.Create(new Position { X=2f });

        World.Destroy(B);

        Assert.True(World.IsAlive(A));
        Assert.Equal(1,World.Archetype0.Count);
        Assert.Equal(1f,World.Archetype0.GetPositionRefUnchecked(0).X);
    }

    [Fact]
    public void DeferredDestroyIsAppliedAtThePlaybackBarrier(){
        World World=new();
        State State=new(World){ Enabled=true };
        var Entity=World.Create(new Position());

        World.DestroyDeferred(Entity,ref State);

        Assert.True(World.IsAlive(Entity));

        World.PlaybackStructuralChanges(ref State);

        Assert.False(World.IsAlive(Entity));
    }

    [Fact]
    public void DeferredDestroyQueueIsDrained(){
        World World=new();
        State State=new(World){ Enabled=true };
        var Entity=World.Create(new Position());

        World.DestroyDeferred(Entity,ref State);
        World.PlaybackStructuralChanges(ref State);
        World.PlaybackStructuralChanges(ref State);

        Assert.False(World.IsAlive(Entity));
    }

    [Fact]
    public void MoveThenDestroyInTheSameFrame_LeavesTheEntityDestroyed(){
        // プレイバックはMoveを先に、Destroyを後に適用する。逆順だと、破棄後の墓石行へ移動を
        // 適用して生き返らせてしまう。
        World World=new();
        State State=new(World){ Enabled=true };
        var Entity=World.Create(new Position { X=1f });

        World.MoveArchetype(Entity,new Position { X=9f },new Velocity { X=1f },ref State);
        World.DestroyDeferred(Entity,ref State);
        World.PlaybackStructuralChanges(ref State);

        Assert.False(World.IsAlive(Entity));
        Assert.Equal(0,World.Archetype1.Count);
    }

    [Fact]
    public void MoveAppliedToAnAlreadyDestroyedEntityIsIgnored(){
        World World=new();
        State State=new(World){ Enabled=true };
        var Entity=World.Create(new Position { X=1f });

        World.MoveArchetype(Entity,new Position { X=9f },new Velocity { X=1f },ref State);
        World.Destroy(Entity);
        World.PlaybackStructuralChanges(ref State);

        Assert.False(World.IsAlive(Entity));
        Assert.Equal(0,World.Archetype1.Count);
    }
}
