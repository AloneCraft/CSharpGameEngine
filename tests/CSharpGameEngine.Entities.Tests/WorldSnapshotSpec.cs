using System.Security.Cryptography;
using System.Text;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Entities.Tests;

public sealed class WorldSnapshotSpec {
    private static readonly WorldCellId CellA=WorldCellId.Parse("30000000-0000-5000-8000-000000000001");
    private static readonly WorldCellId CellB=WorldCellId.Parse("30000000-0000-5000-8000-000000000002");
    private static readonly PersistentEntityId EntityA=PersistentEntityId.Parse("40000000-0000-5000-8000-000000000001");
    private static readonly PersistentEntityId EntityB=PersistentEntityId.Parse("40000000-0000-5000-8000-000000000002");
    private static readonly string EventHash=Hash("events");

    [Fact]
    public void RestoreReturnsTheExactCanonicalCellState(){
        World World=new();
        State State=new(World){Enabled=true};
        Entity First=World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f,Y=2f,Z=3f}));
        World.SetEntityEnabled(First,false);
        byte[]Snapshot=World.CaptureSnapshot(42,EventHash);

        World.MoveArchetype(First,new Position{X=99f},ref State);
        World.PlaybackStructuralChanges(ref State);
        World.SetEntityEnabled(First,true);

        EcsSnapshotRestoreResult Result=World.RestoreSnapshot(Snapshot);

        Assert.True(Result.Succeeded,Result.Error.Message);
        Assert.Equal(1,Result.AppliedEntityCount);
        Assert.Null(Result.Pending);
        Assert.True(World.TryGetComponent(First,out Position Restored));
        Assert.Equal(1f,Restored.X);
        Assert.False(World.IsEntityEnabled(First));
        Assert.Equal(Snapshot,World.CaptureSnapshot(42,EventHash));
    }

    [Fact]
    public void CorruptionAndUnknownSchemaAreRejectedBeforeMutation(){
        World World=new();
        State State=new(World){Enabled=true};
        Entity First=World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f}));
        byte[]Snapshot=World.CaptureSnapshot(7,EventHash);
        World.MoveArchetype(First,new Position{X=9f},ref State);
        World.PlaybackStructuralChanges(ref State);

        byte[]Corrupt=(byte[])Snapshot.Clone();
        Corrupt[^1]^=0x5a;
        EcsSnapshotRestoreResult CorruptResult=World.RestoreSnapshot(Corrupt);
        Assert.False(CorruptResult.Succeeded);
        Assert.Equal(EcsSnapshotErrorCode.ChecksumMismatch,CorruptResult.Error.Code);
        Assert.True(World.TryGetComponent(First,out Position AfterCorruption));
        Assert.Equal(9f,AfterCorruption.X);

        byte[]WrongSchema=(byte[])Snapshot.Clone();
        WrongSchema[16]^=0x01;
        EcsSnapshotRestoreResult SchemaResult=World.RestoreSnapshot(WrongSchema);
        Assert.False(SchemaResult.Succeeded);
        Assert.Equal(EcsSnapshotErrorCode.SchemaMismatch,SchemaResult.Error.Code);
        Assert.True(World.TryGetComponent(First,out Position AfterSchema));
        Assert.Equal(9f,AfterSchema.X);
    }

    [Fact]
    public void RestoreRemovesEntitiesAbsentFromTheCapturedCell(){
        World World=new();
        World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f}));
        byte[]Snapshot=World.CaptureSnapshot(1,EventHash);
        Entity Added=World.AttachToCell(CellA,EntityB,World.Create(new Position{X=2f}));

        EcsSnapshotRestoreResult Result=World.RestoreSnapshot(Snapshot);

        Assert.True(Result.Succeeded,Result.Error.Message);
        Assert.Equal(1,Result.RemovedEntityCount);
        Assert.False(World.IsAlive(Added));
        Assert.False(World.TryResolve(EntityB,out _));
    }

    [Fact]
    public void UpdatePreservesUnloadedCellDeltaUntilThatCellLoadsAgain(){
        World Source=new();
        State SourceState=new(Source){Enabled=true};
        Entity Loaded=Source.AttachToCell(CellA,EntityA,Source.Create(new Position{X=1f}));
        Source.AttachToCell(CellB,EntityB,Source.Create(new Position{X=2f}));
        byte[]Initial=Source.CaptureSnapshot(10,EventHash);
        Source.UnloadCell(CellB);
        Source.MoveArchetype(Loaded,new Position{X=10f},ref SourceState);
        Source.PlaybackStructuralChanges(ref SourceState);
        byte[]Merged=Source.UpdateSnapshot(Initial,11,EventHash);

        Assert.True(EcsSnapshotArtifactCodec.TryRead(Merged,World.SnapshotSchemaFingerprint,out EcsSnapshotDocument?Document,out EcsSnapshotError ReadError),ReadError.Message);
        Assert.Equal(2,Document!.CapturedCells.Count);
        Assert.Equal(2,Document.Entities.Count);

        World Target=new();
        Entity TargetA=Target.AttachToCell(CellA,EntityA,Target.Create(new Position{X=-1f}));
        EcsSnapshotRestoreResult FirstPass=Target.RestoreSnapshot(Merged);
        Assert.True(FirstPass.Succeeded,FirstPass.Error.Message);
        Assert.NotNull(FirstPass.Pending);
        Assert.Equal(1,FirstPass.Pending!.CellCount);
        Assert.True(Target.TryGetComponent(TargetA,out Position RestoredA));
        Assert.Equal(10f,RestoredA.X);

        Entity TargetB=Target.AttachToCell(CellB,EntityB,Target.Create(new Position{X=-2f}));
        EcsSnapshotRestoreResult SecondPass=Target.ApplyPendingSnapshot(FirstPass.Pending);
        Assert.True(SecondPass.Succeeded,SecondPass.Error.Message);
        Assert.Null(SecondPass.Pending);
        Assert.True(Target.TryGetComponent(TargetB,out Position RestoredB));
        Assert.Equal(2f,RestoredB.X);
    }

    [Fact]
    public void DerivedRuntimeEntityLinksAreNotStoredAsAuthoritativeState(){
        World World=new();
        Entity ParentEntity=World.Create(new Position{X=4f});
        World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f},new Parent{Value=ParentEntity}));

        byte[]Snapshot=World.CaptureSnapshot(5,EventHash);

        Assert.True(EcsSnapshotArtifactCodec.TryRead(Snapshot,World.SnapshotSchemaFingerprint,out EcsSnapshotDocument?Document,out EcsSnapshotError Error),Error.Message);
        EcsSnapshotEntityData Entry=Assert.Single(Document!.Entities);
        EcsSnapshotComponentData Component=Assert.Single(Entry.Components);
        Assert.Equal(typeof(Position).FullName,Component.TypeId);
        Assert.DoesNotContain(typeof(Parent).FullName!,Encoding.UTF8.GetString(Snapshot),StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureRejectsAPartialStructuralTick(){
        World World=new();
        Entity Entity=World.AttachToCell(CellA,EntityA,World.Create(new Position()));
        World.Commands.Destroy(Entity);

        Assert.Throws<InvalidOperationException>(()=>World.CaptureSnapshot(1,EventHash));
    }

    [Fact]
    public void CanonicalEnvelopeWithInvalidComponentPayloadIsRejectedBeforeMutation(){
        World World=new();
        State State=new(World){Enabled=true};
        Entity First=World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f}));
        byte[]Snapshot=World.CaptureSnapshot(12,EventHash);
        Assert.True(EcsSnapshotArtifactCodec.TryRead(Snapshot,World.SnapshotSchemaFingerprint,out EcsSnapshotDocument?Document,out EcsSnapshotError ReadError),ReadError.Message);
        EcsSnapshotEntityData Saved=Assert.Single(Document!.Entities);
        EcsSnapshotComponentData PositionData=Assert.Single(Saved.Components);
        var Invalid=Saved with{Components=[PositionData with{Payload=[0x01]}]};
        byte[]Forged=EcsSnapshotArtifactCodec.Write(World.SnapshotSchemaFingerprint,Document.Tick,Document.EventHash,Document.CapturedCells,[Invalid]);
        World.MoveArchetype(First,new Position{X=9f},ref State);
        World.PlaybackStructuralChanges(ref State);

        EcsSnapshotRestoreResult Result=World.RestoreSnapshot(Forged);

        Assert.False(Result.Succeeded);
        Assert.Equal(EcsSnapshotErrorCode.ComponentMismatch,Result.Error.Code);
        Assert.True(World.TryGetComponent(First,out Position After));
        Assert.Equal(9f,After.X);
    }

    [Fact]
    public void MissingSavedEntityRejectsTheWholeLoadedCellBeforeApplyingEarlierEntries(){
        World World=new();
        State State=new(World){Enabled=true};
        Entity First=World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f}));
        Entity Second=World.AttachToCell(CellA,EntityB,World.Create(new Position{X=2f}));
        byte[]Snapshot=World.CaptureSnapshot(13,EventHash);
        World.MoveArchetype(First,new Position{X=9f},ref State);
        World.PlaybackStructuralChanges(ref State);
        World.Destroy(Second);

        EcsSnapshotRestoreResult Result=World.RestoreSnapshot(Snapshot);

        Assert.False(Result.Succeeded);
        Assert.Equal(EcsSnapshotErrorCode.EntityMissing,Result.Error.Code);
        Assert.True(World.TryGetComponent(First,out Position After));
        Assert.Equal(9f,After.X);
    }

    [Fact]
    public void ArchetypeMismatchIsRejectedWithoutMovingOrMutatingTheEntity(){
        World World=new();
        State State=new(World){Enabled=true};
        Entity First=World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f}));
        byte[]Snapshot=World.CaptureSnapshot(14,EventHash);
        World.MoveArchetype(First,new Position{X=9f},new Velocity{X=3f},ref State);
        World.PlaybackStructuralChanges(ref State);

        EcsSnapshotRestoreResult Result=World.RestoreSnapshot(Snapshot);

        Assert.False(Result.Succeeded);
        Assert.Equal(EcsSnapshotErrorCode.ArchetypeMismatch,Result.Error.Code);
        Assert.True(World.TryGetComponent(First,out Position AfterPosition));
        Assert.True(World.TryGetComponent(First,out Velocity AfterVelocity));
        Assert.Equal(9f,AfterPosition.X);
        Assert.Equal(3f,AfterVelocity.X);
    }

    [Fact]
    public void RestoreRejectsAPartialStructuralTickWithoutApplyingCommands(){
        World World=new();
        Entity First=World.AttachToCell(CellA,EntityA,World.Create(new Position{X=1f}));
        byte[]Snapshot=World.CaptureSnapshot(15,EventHash);
        World.Commands.Destroy(First);

        EcsSnapshotRestoreResult Result=World.RestoreSnapshot(Snapshot);

        Assert.False(Result.Succeeded);
        Assert.Equal(EcsSnapshotErrorCode.WorldBusy,Result.Error.Code);
        Assert.True(World.IsAlive(First));
        Assert.True(World.TryResolve(EntityA,out Entity Resolved));
        Assert.Equal(First,Resolved);
    }

    private static string Hash(string Value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Value))).ToLowerInvariant();
}
