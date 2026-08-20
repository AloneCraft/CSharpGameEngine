namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// <see cref="SceneInstance"/>の所有権とアンロード(ADR-0040)。
///
/// <para>
/// ここで確かめたい中核は「同じScene定義を何度でもinstantiateでき、それぞれ独立していること」と
/// 「1つのWorldに複数SceneInstanceが同時に存在できること」、そして<b>「現在のScene」という暗黙の
/// グローバル状態がどこにも無いこと</b>である。生成コード(Phase 4)はここで検証したAPIを呼ぶだけになる。
/// </para>
/// </summary>
public sealed class SceneInstanceSpec {
    /// <summary>生成された<c>Instantiate</c>が行うことを手で書いたもの。Phase 4以降はSGがこれを出す。</summary>
    private static (SceneInstance Instance,Entity Root,Entity Child)Instantiate(ref World World,float Seed){
        var Instance=World.BeginSceneInstance();
        var Root=World.AttachToScene(Instance,World.Create(new Position { X=Seed }));
        var Child=World.AttachToScene(Instance,World.Create(new Position { X=Seed+1f },new Parent { Value=Root }));
        World.EndSceneInstance(Instance,Root);
        return (Instance,Root,Child);
    }

    [Fact]
    public void InstantiatingTheSameSceneTwiceProducesIndependentInstances(){
        World World=new();

        (var A,var RootA,_)=Instantiate(ref World,10f);
        (var B,var RootB,_)=Instantiate(ref World,20f);

        Assert.NotEqual(A,B);
        Assert.NotEqual(RootA,RootB);
        Assert.True(World.IsSceneAlive(A));
        Assert.True(World.IsSceneAlive(B));
        Assert.Equal(RootA,World.GetSceneRoot(A));
        Assert.Equal(RootB,World.GetSceneRoot(B));
    }

    [Fact]
    public void UnloadingOneSceneLeavesTheOtherUntouched(){
        World World=new();
        (var A,var RootA,var ChildA)=Instantiate(ref World,10f);
        (var B,var RootB,var ChildB)=Instantiate(ref World,20f);

        World.UnloadScene(A);

        Assert.False(World.IsSceneAlive(A));
        Assert.False(World.IsAlive(RootA));
        Assert.False(World.IsAlive(ChildA));
        Assert.True(World.IsSceneAlive(B));
        Assert.True(World.IsAlive(RootB));
        Assert.True(World.IsAlive(ChildB));
    }

    [Fact]
    public void EntitiesReportTheSceneThatCreatedThem(){
        World World=new();
        (var A,var RootA,var ChildA)=Instantiate(ref World,10f);

        Assert.Equal(A,World.GetOwningScene(RootA));
        Assert.Equal(A,World.GetOwningScene(ChildA));
    }

    [Fact]
    public void WorldOwnedEntitiesBelongToNoScene(){
        // Sceneに属さないWorld直属Entityも許可する。World.Createはそのまま使える。
        World World=new();

        var Loose=World.Create(new Position { X=1f });

        Assert.True(World.IsAlive(Loose));
        Assert.True(World.GetOwningScene(Loose).IsNone);
    }

    [Fact]
    public void GameplayCanAttachARuntimeEntityToAScene(){
        World World=new();
        (var A,_,_)=Instantiate(ref World,10f);

        var Spawned=World.AttachToScene(A,World.Create(new Position { X=99f }));

        Assert.Equal(A,World.GetOwningScene(Spawned));

        World.UnloadScene(A);

        Assert.False(World.IsAlive(Spawned));
    }

    [Fact]
    public void HandlesToAnUnloadedSceneAreRejected(){
        World World=new();
        (var A,_,_)=Instantiate(ref World,10f);

        World.UnloadScene(A);

        Assert.False(World.IsSceneAlive(A));
        Assert.Equal(default,World.GetSceneRoot(A));

        // 二重アンロードは黙って無視される。
        World.UnloadScene(A);
    }

    [Fact]
    public void ReusedSlotDoesNotResurrectAnOldHandle(){
        // SceneスロットはEntity Idと違って再利用する。だからこそ世代が要る(ABA問題)。
        World World=new();
        (var A,_,_)=Instantiate(ref World,10f);
        World.UnloadScene(A);

        (var B,var RootB,_)=Instantiate(ref World,20f);

        Assert.Equal(A.Slot,B.Slot);
        Assert.NotEqual(A.Version,B.Version);
        Assert.False(World.IsSceneAlive(A));
        Assert.True(World.IsSceneAlive(B));
        Assert.Equal(RootB,World.GetSceneRoot(B));
    }

    [Fact]
    public void DefaultAndNoneHandlesAreNeverAlive(){
        World World=new();
        Instantiate(ref World,10f);

        Assert.False(World.IsSceneAlive(default));
        Assert.False(World.IsSceneAlive(SceneInstance.None));
        Assert.True(SceneInstance.None.IsNone);
    }

    [Fact]
    public void EntityDestroyedByGameplayIsSkippedDuringUnload(){
        World World=new();
        (var A,var RootA,var ChildA)=Instantiate(ref World,10f);

        World.Destroy(ChildA);
        World.UnloadScene(A);

        Assert.False(World.IsAlive(RootA));
        Assert.False(World.IsAlive(ChildA));
        Assert.False(World.IsSceneAlive(A));
    }

    [Fact]
    public void DeferredUnloadIsAppliedAtThePlaybackBarrier(){
        World World=new();
        State State=new(World){ Enabled=true };
        (var A,var RootA,_)=Instantiate(ref World,10f);

        World.UnloadSceneDeferred(A,ref State);

        Assert.True(World.IsAlive(RootA));

        World.PlaybackStructuralChanges(ref State);

        Assert.False(World.IsAlive(RootA));
        Assert.False(World.IsSceneAlive(A));
    }

    [Fact]
    public void ThreeScenesCoexistInOneWorld(){
        World World=new();

        (var A,var RootA,_)=Instantiate(ref World,10f);
        (var B,var RootB,_)=Instantiate(ref World,20f);
        (var C,var RootC,_)=Instantiate(ref World,30f);

        Assert.True(World.IsSceneAlive(A));
        Assert.True(World.IsSceneAlive(B));
        Assert.True(World.IsSceneAlive(C));

        World.UnloadScene(B);

        Assert.True(World.IsAlive(RootA));
        Assert.False(World.IsAlive(RootB));
        Assert.True(World.IsAlive(RootC));
    }

    [Fact]
    public void ArchetypeMoveKeepsSceneOwnership(){
        // 位置表をタプルごと代入するとSceneSlotが消え、Archetype移動しただけでSceneから外れてしまう。
        World World=new();
        State State=new(World){ Enabled=true };
        (var A,var RootA,_)=Instantiate(ref World,10f);

        World.MoveArchetype(RootA,new Position { X=5f },new Velocity { X=1f },ref State);
        World.PlaybackStructuralChanges(ref State);

        Assert.Equal(A,World.GetOwningScene(RootA));

        World.UnloadScene(A);

        Assert.False(World.IsAlive(RootA));
    }

    [Fact]
    public void ReattachingAnEntityMovesOwnershipAndTheOldSceneNoLongerDestroysIt(){
        // 所有権は位置表のSceneSlotが正で、SceneInstanceSlot.Entitiesは追記のみの記録である。
        // 付け替え元のUnloadが新しいSceneのEntityを巻き添えに壊してはならない。
        World World=new();
        (var A,_,_)=Instantiate(ref World,10f);
        (var B,_,_)=Instantiate(ref World,20f);
        var Moved=World.AttachToScene(A,World.Create(new Position { X=99f }));

        World.AttachToScene(B,Moved);

        Assert.Equal(B,World.GetOwningScene(Moved));

        World.UnloadScene(A);

        Assert.True(World.IsAlive(Moved));

        World.UnloadScene(B);

        Assert.False(World.IsAlive(Moved));
    }

    [Fact]
    public void ParentHandlesDangleAfterUnload_WhichIsWhyIsAliveMustGuardHierarchyWalks(){
        // 閉集合Archetypeでは親のEntityハンドルがComponentデータの中に生で入る。親を含むSceneを
        // アンロードすると、他Scene/World直属の子が持つParent.Valueは死んだEntityを指す。
        // 親子を辿るSystemは必ずIsAliveでガードすること(ADR-0040 既知の制約)。
        World World=new();
        (var A,var RootA,_)=Instantiate(ref World,10f);
        var Outsider=World.Create(new Position { X=1f },new Parent { Value=RootA });

        World.UnloadScene(A);

        Assert.True(World.IsAlive(Outsider));
        Assert.False(World.IsAlive(RootA));
    }
}
