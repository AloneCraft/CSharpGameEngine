namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// Entity破棄(ADR-0040、案A: 墓石のみでIDは再利用しない)。
///
/// <para>
/// 配列は2種類あり、混同してはならない。<b>位置表</b>(<c>World.EntityArchetypeIndices</c>)は行番号が
/// そのまま<c>Entity.Id</c>で、追記のみでswap-backしない。<b>ArchetypeのSoA列</b>は行番号が<c>Index</c>で、
/// swap-backはこちらだけで起きる。破棄したEntityの位置表の行には墓石が残り、そのIdは二度と
/// 再配布されないため、古いハンドルが<b>別のEntityに化けることは構造的に起こらない</b>。
/// </para>
/// </summary>
public sealed class EntityDestroySpec {
    [Fact]
    public void CreatedEntityIsAlive(){
        World world=new();

        Entity entity=world.Create(new Position { X=1f });

        Assert.True(world.IsAlive(entity));
    }

    [Fact]
    public void DestroyedEntityIsNoLongerAlive(){
        World world=new();
        Entity entity=world.Create(new Position { X=1f });

        world.Destroy(entity);

        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void DestroyIsIdempotent(){
        World world=new();
        Entity entity=world.Create(new Position());

        world.Destroy(entity);
        world.Destroy(entity);

        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void NeverSeenEntityIsNotAlive(){
        World world=new();

        Assert.False(world.IsAlive(new Entity(0)));
        Assert.False(world.IsAlive(new Entity(999)));
        Assert.False(world.IsAlive(new Entity(-1)));
    }

    [Fact]
    public void DestroyedIdIsNeverHandedOutAgain(){
        // これが「世代を持たなくてもstale handleが別Entityに化けない」ことの根拠である。
        World world=new();
        Entity first=world.Create(new Position { X=1f });
        world.Destroy(first);

        Entity second=world.Create(new Position { X=2f });

        Assert.NotEqual(first.Id,second.Id);
        Assert.False(world.IsAlive(first));
        Assert.True(world.IsAlive(second));
    }

    [Fact]
    public void SurvivingEntityKeepsItsIdentityAfterASwapBack(){
        // 末尾のEntityが消えた行へ座り直すが、Entity.Idは動かない。動くのはArchetype内の座席番号だけ。
        World world=new();
        Entity a=world.Create(new Position { X=1f });
        Entity b=world.Create(new Position { X=2f });
        Entity c=world.Create(new Position { X=3f });

        world.Destroy(b);

        Assert.True(world.IsAlive(a));
        Assert.False(world.IsAlive(b));
        Assert.True(world.IsAlive(c));
        Assert.Equal(3f,world.Archetype0.GetPositionRefUnchecked(world.Archetype0.Count-1).X);
        Assert.Equal(2,world.Archetype0.Count);
    }

    [Fact]
    public void DestroyingTheLastEntityDoesNotDisturbTheOthers(){
        World world=new();
        Entity a=world.Create(new Position { X=1f });
        Entity b=world.Create(new Position { X=2f });

        world.Destroy(b);

        Assert.True(world.IsAlive(a));
        Assert.Equal(1,world.Archetype0.Count);
        Assert.Equal(1f,world.Archetype0.GetPositionRefUnchecked(0).X);
    }

    [Fact]
    public void DeferredDestroyIsAppliedAtThePlaybackBarrier(){
        World world=new();
        State state=new(world){ Enabled=true };
        Entity entity=world.Create(new Position());

        world.DestroyDeferred(entity,ref state);

        Assert.True(world.IsAlive(entity));

        world.PlaybackStructuralChanges(ref state);

        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void DeferredDestroyQueueIsDrained(){
        World world=new();
        State state=new(world){ Enabled=true };
        Entity entity=world.Create(new Position());

        world.DestroyDeferred(entity,ref state);
        world.PlaybackStructuralChanges(ref state);
        world.PlaybackStructuralChanges(ref state);

        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void MoveThenDestroyInTheSameFrame_LeavesTheEntityDestroyed(){
        // プレイバックはMoveを先に、Destroyを後に適用する。逆順だと、破棄後の墓石行へ移動を
        // 適用して生き返らせてしまう。
        World world=new();
        State state=new(world){ Enabled=true };
        Entity entity=world.Create(new Position { X=1f });

        world.MoveArchetype(entity,new Position { X=9f },new Velocity { X=1f },ref state);
        world.DestroyDeferred(entity,ref state);
        world.PlaybackStructuralChanges(ref state);

        Assert.False(world.IsAlive(entity));
        Assert.Equal(0,world.Archetype1.Count);
    }

    [Fact]
    public void MoveAppliedToAnAlreadyDestroyedEntityIsIgnored(){
        World world=new();
        State state=new(world){ Enabled=true };
        Entity entity=world.Create(new Position { X=1f });

        world.MoveArchetype(entity,new Position { X=9f },new Velocity { X=1f },ref state);
        world.Destroy(entity);
        world.PlaybackStructuralChanges(ref state);

        Assert.False(world.IsAlive(entity));
        Assert.Equal(0,world.Archetype1.Count);
    }
}
