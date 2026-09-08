using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Entities.Tests;

public sealed class EntityCommandBufferSpec {
    private static readonly WorldCellId CellA=WorldCellId.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly WorldCellId CellB=WorldCellId.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly PersistentEntityId PersistentA=PersistentEntityId.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public void StructuralCommandsPreserveTheirGlobalEnqueueOrder(){
        World World=new();
        State State=new(World){ Enabled=true };
        var MoveThenDestroy=World.Create(new Position { X=1f });
        var DestroyThenMove=World.Create(new Position { X=2f });
        World.MoveArchetype(MoveThenDestroy,new Position { X=3f },new Velocity(),ref State);
        World.Commands.Destroy(MoveThenDestroy);
        World.Commands.Destroy(DestroyThenMove);
        World.MoveArchetype(DestroyThenMove,new Position { X=4f },new Velocity(),ref State);
        World.PlaybackStructuralChanges(ref State);
        Assert.False(World.IsAlive(MoveThenDestroy));
        Assert.False(World.IsAlive(DestroyThenMove));
        Assert.Equal(0,World.Archetype1.Count);
    }

    [Fact]
    public void SceneAttachAndUnloadOutcomeDependsOnlyOnGlobalEnqueueOrder(){
        World AttachThenUnloadWorld=new();
        State AttachThenUnloadState=new(AttachThenUnloadWorld){ Enabled=true };
        SceneInstance FirstOwner=AttachThenUnloadWorld.BeginSceneInstance();
        SceneInstance NewOwner=AttachThenUnloadWorld.BeginSceneInstance();
        Entity Survives=AttachThenUnloadWorld.AttachToScene(
            FirstOwner,AttachThenUnloadWorld.Create(new Position()));
        AttachThenUnloadWorld.AttachToSceneDeferred(NewOwner,Survives);
        AttachThenUnloadWorld.Commands.UnloadScene(FirstOwner);
        AttachThenUnloadWorld.PlaybackStructuralChanges(ref AttachThenUnloadState);
        Assert.True(AttachThenUnloadWorld.IsAlive(Survives));
        Assert.Equal(NewOwner,AttachThenUnloadWorld.GetOwningScene(Survives));

        World UnloadThenAttachWorld=new();
        State UnloadThenAttachState=new(UnloadThenAttachWorld){ Enabled=true };
        SceneInstance RemovedOwner=UnloadThenAttachWorld.BeginSceneInstance();
        SceneInstance RejectedOwner=UnloadThenAttachWorld.BeginSceneInstance();
        Entity Destroyed=UnloadThenAttachWorld.AttachToScene(
            RemovedOwner,UnloadThenAttachWorld.Create(new Position()));
        UnloadThenAttachWorld.Commands.UnloadScene(RemovedOwner);
        UnloadThenAttachWorld.AttachToSceneDeferred(RejectedOwner,Destroyed);
        UnloadThenAttachWorld.PlaybackStructuralChanges(ref UnloadThenAttachState);
        Assert.False(UnloadThenAttachWorld.IsAlive(Destroyed));
        Assert.True(UnloadThenAttachWorld.IsSceneAlive(RejectedOwner));
    }

    [Fact]
    public void DirectStructuralMutationIsRejectedDuringSystemExecution(){
        World World=new();
        State State=new(World){ Enabled=true };
        var Entity=World.Create(new Position());
        Entity Deferred=default;
        World.BeginSystemExecution();
        try {
            Assert.Throws<InvalidOperationException>(()=>World.Create(new Position()));
            Assert.Throws<InvalidOperationException>(()=>World.Destroy(Entity));
            Assert.Throws<InvalidOperationException>(()=>World.BeginSceneInstance());
            Deferred=World.CreateDeferred(new Position { X=7f });
            Assert.False(World.IsAlive(Deferred));
            World.Commands.Destroy(Entity);
        } finally {
            World.EndSystemExecution();
        }
        World.PlaybackStructuralChanges(ref State);
        Assert.False(World.IsAlive(Entity));
        Assert.True(World.IsAlive(Deferred));
        Assert.True(World.TryGetComponent(Deferred,out Position Position));
        Assert.Equal(7f,Position.X);
    }

    [Fact]
    public void CellUnloadAtPlaybackInvalidatesRuntimeAndPersistentHandles(){
        World World=new();
        State State=new(World){ Enabled=true };
        var Entity=World.AttachToCell(CellA,PersistentA,World.Create(new Position { X=1f }));
        Assert.Equal(CellA,World.GetOwningCell(Entity));
        Assert.Equal(PersistentA,World.GetPersistentEntityId(Entity));
        Assert.True(World.TryResolve(PersistentA,out var Resolved));
        Assert.Equal(Entity,Resolved);
        World.Commands.UnloadCell(CellA);
        Assert.True(World.IsAlive(Entity));
        Assert.Equal(1,World.GetStatistics().PendingCellUnloadCount);
        World.PlaybackStructuralChanges(ref State);
        Assert.False(World.IsAlive(Entity));
        Assert.False(World.TryResolve(PersistentA,out _));
        Assert.Equal(0,World.GetStatistics().PendingCellUnloadCount);
        var Replacement=World.Create(new Position { X=2f });
        Assert.Equal(Entity.Index,Replacement.Index);
        Assert.NotEqual(Entity.Generation,Replacement.Generation);
        Assert.False(World.TryResolve(PersistentA,out _));
    }

    [Fact]
    public void SceneAndCellOwnershipAreMutuallyExclusive(){
        World World=new();
        var Scene=World.BeginSceneInstance();
        var Entity=World.AttachToScene(Scene,World.Create(new Position()));
        World.EndSceneInstance(Scene,Entity);
        World.AttachToCell(CellA,PersistentA,Entity);
        Assert.True(World.GetOwningScene(Entity).IsNone);
        Assert.Equal(CellA,World.GetOwningCell(Entity));
        World.UnloadScene(Scene);
        Assert.True(World.IsAlive(Entity));
        var SceneB=World.BeginSceneInstance();
        World.AttachToScene(SceneB,Entity);
        World.EndSceneInstance(SceneB,Entity);
        Assert.Equal(SceneB,World.GetOwningScene(Entity));
        Assert.False(World.GetOwningCell(Entity).IsValid);
        Assert.True(World.TryResolve(PersistentA,out var Resolved));
        Assert.Equal(Entity,Resolved);
        World.UnloadCell(CellA);
        Assert.True(World.IsAlive(Entity));
        World.UnloadScene(SceneB);
        Assert.False(World.IsAlive(Entity));
    }

    [Fact]
    public void DuplicatePersistentIdentityIsRejectedWithoutChangingEitherOwner(){
        World World=new();
        var First=World.AttachToCell(CellA,PersistentA,World.Create(new Position { X=1f }));
        var Second=World.Create(new Position { X=2f });
        Assert.Throws<InvalidOperationException>(()=>World.AttachToCell(CellB,PersistentA,Second));
        Assert.True(World.TryResolve(PersistentA,out var Resolved));
        Assert.Equal(First,Resolved);
        Assert.False(World.GetOwningCell(Second).IsValid);
        Assert.False(World.GetPersistentEntityId(Second).IsValid);
    }
}
