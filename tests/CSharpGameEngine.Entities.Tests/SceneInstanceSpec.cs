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
    private static (SceneInstance Instance,Entity Root,Entity Child)Instantiate(ref World world,float Seed){
        SceneInstance Instance=world.BeginSceneInstance();
        Entity Root=world.AttachToScene(Instance,world.Create(new Position { X=Seed }));
        Entity Child=world.AttachToScene(Instance,world.Create(new Position { X=Seed+1f },new Parent { Value=Root }));
        world.EndSceneInstance(Instance,Root);
        return (Instance,Root,Child);
    }

    [Fact]
    public void InstantiatingTheSameSceneTwiceProducesIndependentInstances(){
        World world=new();

        (SceneInstance A,Entity RootA,_)=Instantiate(ref world,10f);
        (SceneInstance B,Entity RootB,_)=Instantiate(ref world,20f);

        Assert.NotEqual(A,B);
        Assert.NotEqual(RootA,RootB);
        Assert.True(world.IsSceneAlive(A));
        Assert.True(world.IsSceneAlive(B));
        Assert.Equal(RootA,world.GetSceneRoot(A));
        Assert.Equal(RootB,world.GetSceneRoot(B));
    }

    [Fact]
    public void UnloadingOneSceneLeavesTheOtherUntouched(){
        World world=new();
        (SceneInstance A,Entity RootA,Entity ChildA)=Instantiate(ref world,10f);
        (SceneInstance B,Entity RootB,Entity ChildB)=Instantiate(ref world,20f);

        world.UnloadScene(A);

        Assert.False(world.IsSceneAlive(A));
        Assert.False(world.IsAlive(RootA));
        Assert.False(world.IsAlive(ChildA));
        Assert.True(world.IsSceneAlive(B));
        Assert.True(world.IsAlive(RootB));
        Assert.True(world.IsAlive(ChildB));
    }

    [Fact]
    public void EntitiesReportTheSceneThatCreatedThem(){
        World world=new();
        (SceneInstance A,Entity RootA,Entity ChildA)=Instantiate(ref world,10f);

        Assert.Equal(A,world.GetOwningScene(RootA));
        Assert.Equal(A,world.GetOwningScene(ChildA));
    }

    [Fact]
    public void WorldOwnedEntitiesBelongToNoScene(){
        // Sceneに属さないWorld直属Entityも許可する。World.Createはそのまま使える。
        World world=new();

        Entity Loose=world.Create(new Position { X=1f });

        Assert.True(world.IsAlive(Loose));
        Assert.True(world.GetOwningScene(Loose).IsNone);
    }

    [Fact]
    public void GameplayCanAttachARuntimeEntityToAScene(){
        World world=new();
        (SceneInstance A,_,_)=Instantiate(ref world,10f);

        Entity Spawned=world.AttachToScene(A,world.Create(new Position { X=99f }));

        Assert.Equal(A,world.GetOwningScene(Spawned));

        world.UnloadScene(A);

        Assert.False(world.IsAlive(Spawned));
    }

    [Fact]
    public void HandlesToAnUnloadedSceneAreRejected(){
        World world=new();
        (SceneInstance A,_,_)=Instantiate(ref world,10f);

        world.UnloadScene(A);

        Assert.False(world.IsSceneAlive(A));
        Assert.Equal(default,world.GetSceneRoot(A));

        // 二重アンロードは黙って無視される。
        world.UnloadScene(A);
    }

    [Fact]
    public void ReusedSlotDoesNotResurrectAnOldHandle(){
        // SceneスロットはEntity Idと違って再利用する。だからこそ世代が要る(ABA問題)。
        World world=new();
        (SceneInstance A,_,_)=Instantiate(ref world,10f);
        world.UnloadScene(A);

        (SceneInstance B,Entity RootB,_)=Instantiate(ref world,20f);

        Assert.Equal(A.Slot,B.Slot);
        Assert.NotEqual(A.Version,B.Version);
        Assert.False(world.IsSceneAlive(A));
        Assert.True(world.IsSceneAlive(B));
        Assert.Equal(RootB,world.GetSceneRoot(B));
    }

    [Fact]
    public void DefaultAndNoneHandlesAreNeverAlive(){
        World world=new();
        Instantiate(ref world,10f);

        Assert.False(world.IsSceneAlive(default));
        Assert.False(world.IsSceneAlive(SceneInstance.None));
        Assert.True(SceneInstance.None.IsNone);
    }

    [Fact]
    public void EntityDestroyedByGameplayIsSkippedDuringUnload(){
        World world=new();
        (SceneInstance A,Entity RootA,Entity ChildA)=Instantiate(ref world,10f);

        world.Destroy(ChildA);
        world.UnloadScene(A);

        Assert.False(world.IsAlive(RootA));
        Assert.False(world.IsAlive(ChildA));
        Assert.False(world.IsSceneAlive(A));
    }

    [Fact]
    public void DeferredUnloadIsAppliedAtThePlaybackBarrier(){
        World world=new();
        State state=new(world){ Enabled=true };
        (SceneInstance A,Entity RootA,_)=Instantiate(ref world,10f);

        world.UnloadSceneDeferred(A,ref state);

        Assert.True(world.IsAlive(RootA));

        world.PlaybackStructuralChanges(ref state);

        Assert.False(world.IsAlive(RootA));
        Assert.False(world.IsSceneAlive(A));
    }

    [Fact]
    public void ThreeScenesCoexistInOneWorld(){
        World world=new();

        (SceneInstance A,Entity RootA,_)=Instantiate(ref world,10f);
        (SceneInstance B,Entity RootB,_)=Instantiate(ref world,20f);
        (SceneInstance C,Entity RootC,_)=Instantiate(ref world,30f);

        Assert.True(world.IsSceneAlive(A));
        Assert.True(world.IsSceneAlive(B));
        Assert.True(world.IsSceneAlive(C));

        world.UnloadScene(B);

        Assert.True(world.IsAlive(RootA));
        Assert.False(world.IsAlive(RootB));
        Assert.True(world.IsAlive(RootC));
    }

    [Fact]
    public void ArchetypeMoveKeepsSceneOwnership(){
        // 位置表をタプルごと代入するとSceneSlotが消え、Archetype移動しただけでSceneから外れてしまう。
        World world=new();
        State state=new(world){ Enabled=true };
        (SceneInstance A,Entity RootA,_)=Instantiate(ref world,10f);

        world.MoveArchetype(RootA,new Position { X=5f },new Velocity { X=1f },ref state);
        world.PlaybackStructuralChanges(ref state);

        Assert.Equal(A,world.GetOwningScene(RootA));

        world.UnloadScene(A);

        Assert.False(world.IsAlive(RootA));
    }

    [Fact]
    public void ReattachingAnEntityMovesOwnershipAndTheOldSceneNoLongerDestroysIt(){
        // 所有権は位置表のSceneSlotが正で、SceneInstanceSlot.Entitiesは追記のみの記録である。
        // 付け替え元のUnloadが新しいSceneのEntityを巻き添えに壊してはならない。
        World world=new();
        (SceneInstance A,_,_)=Instantiate(ref world,10f);
        (SceneInstance B,_,_)=Instantiate(ref world,20f);
        Entity Moved=world.AttachToScene(A,world.Create(new Position { X=99f }));

        world.AttachToScene(B,Moved);

        Assert.Equal(B,world.GetOwningScene(Moved));

        world.UnloadScene(A);

        Assert.True(world.IsAlive(Moved));

        world.UnloadScene(B);

        Assert.False(world.IsAlive(Moved));
    }

    [Fact]
    public void ParentHandlesDangleAfterUnload_WhichIsWhyIsAliveMustGuardHierarchyWalks(){
        // 閉集合Archetypeでは親のEntityハンドルがComponentデータの中に生で入る。親を含むSceneを
        // アンロードすると、他Scene/World直属の子が持つParent.Valueは死んだEntityを指す。
        // 親子を辿るSystemは必ずIsAliveでガードすること(ADR-0040 既知の制約)。
        World world=new();
        (SceneInstance A,Entity RootA,_)=Instantiate(ref world,10f);
        Entity Outsider=world.Create(new Position { X=1f },new Parent { Value=RootA });

        world.UnloadScene(A);

        Assert.True(world.IsAlive(Outsider));
        Assert.False(world.IsAlive(RootA));
    }
}
