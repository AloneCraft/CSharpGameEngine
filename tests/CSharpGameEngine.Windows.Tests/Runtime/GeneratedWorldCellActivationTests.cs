using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.World;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class GeneratedWorldCellActivationTests {
    [Fact]
    public async Task StreamingInstantiatesGeneratedSceneWithCellOwnershipAndUnloadsIt(){
        Assert.True(GeneratedWorldCellActivationSink.TryGetPayloadKey(
            "UserEcsProject.Scenes.PrimitiveShowcaseBoxScene",out var PayloadKey));
        var Artifact=WorldCellEntityPayloadCodec.Bake(PayloadKey,new ArtifactProvenance(
            "test","scene://box",Hash256.Compute([1]),"generated-scene",Hash256.Compute([2])));
        var Bytes=ContentArtifactCodec.Write(Artifact);
        var Reference=new AssetArtifactReference(PayloadKey,Artifact.ContentHash);
        var Cell=new WorldCellId(StableId.Parse("ee28ef97-fce6-5f43-8457-18f52987158f"));
        var Definition=new WorldCellDefinition(
            Cell,ReferenceFrameId.Root,
            new FrameLocalBounds(LogicalVector3.Zero,LogicalVector3.One),
            PayloadKey,ArtifactClosure:[Reference]);
        Assert.True(WorldPartitionManifest.TryCreate([Definition],out var Manifest,out var ManifestError),ManifestError.Message);
        var FramesGraph=new ReferenceFrameGraph(ReferenceFrameId.Root);
        Assert.True(FramesGraph.TryCommit(1,out var Frames,out var FrameError),FrameError.Message);
        using var Owner=new WorldOwner();
        var Source=new SingleArtifactSource(Reference,Bytes);
        await using var Streaming=new WorldCellStreamingService(
            new WorldCellRuntime(Manifest),Source,new GeneratedWorldCellActivationSink(Owner));

        var Active=await Streaming.RequestAsync([Cell],Frames,WorldCellStreamingBudget.Unlimited);

        Assert.True(Active.Succeeded,Active.Error.Message);
        Assert.True(Owner.World.HasOwnedCell(Cell));
        Assert.Equal(1,Owner.World.GetStatistics().AliveEntityCount);
        byte[]Snapshot=Owner.World.CaptureSnapshot(Frames.Tick,Hash256.Compute([9]).ToString());
        Assert.True(EcsSnapshotArtifactCodec.TryRead(Snapshot,global::CSharpGameEngine.Entities.World.SnapshotSchemaFingerprint,out var SnapshotDocument,out var SnapshotError),SnapshotError.Message);
        Assert.Equal(Cell,Assert.Single(SnapshotDocument!.CapturedCells));
        Assert.Single(SnapshotDocument.Entities);
        var RuntimeComponentIds=Owner.World.GetType().Assembly.GetTypes()
            .Where(static Type=>Type.IsValueType&&typeof(IRuntimeComponent).IsAssignableFrom(Type))
            .Select(static Type=>Type.FullName)
            .Where(static Name=>Name is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(SnapshotDocument.Entities[0].Components,Component=>RuntimeComponentIds.Contains(Component.TypeId));
        var Persistent=new PersistentEntityId(StableId.FromName(Cell.Value,"UserEcsProject.Scenes.PrimitiveShowcaseBoxScene/(root)"));
        Assert.True(Owner.World.TryResolve(Persistent,out Entity Entity));
        State State=Owner.CreateState();
        var MutatedTransform=new Transform {
            Position=new LogicalVector3(99,98,97),
            Rotation=LogicalQuaternion.Identity,
            Scale=new LogicalVector3(3,4,5),
        };
        var MutatedBounds=new SpatialBounds {
            Center=new LogicalVector3(9,8,7),
            HalfExtents=LogicalVector3.One,
            Layer=new SpatialLayer(3),
        };
        Assert.True(Owner.World.MoveArchetype(Entity,MutatedTransform,new PrimitiveMesh{Type=PrimitiveType.Sphere},MutatedBounds,ref State));
        Owner.World.PlaybackStructuralChanges(ref State);

        EcsSnapshotRestoreResult Restore=Owner.World.RestoreSnapshot(Snapshot);

        Assert.True(Restore.Succeeded,Restore.Error.Message);
        Assert.True(Owner.World.TryGetComponent(Entity,out Transform RestoredTransform));
        Assert.True(Owner.World.TryGetComponent(Entity,out PrimitiveMesh RestoredMesh));
        Assert.True(Owner.World.TryGetComponent(Entity,out SpatialBounds RestoredBounds));
        Assert.Equal(new LogicalVector3(3f,0.5f,0f),RestoredTransform.Position);
        Assert.Equal(LogicalVector3.One,RestoredTransform.Scale);
        Assert.Equal(PrimitiveType.Box,RestoredMesh.Type);
        Assert.Equal(SpatialBounds.UnitCube.Center,RestoredBounds.Center);
        Assert.Equal(SpatialBounds.UnitCube.HalfExtents,RestoredBounds.HalfExtents);
        Assert.Equal(SpatialBounds.UnitCube.Layer,RestoredBounds.Layer);

        var Unloaded=await Streaming.RequestAsync([],Frames,WorldCellStreamingBudget.Unlimited);

        Assert.True(Unloaded.Succeeded,Unloaded.Error.Message);
        Assert.False(Owner.World.HasOwnedCell(Cell));
        Assert.Equal(0,Owner.World.GetStatistics().AliveEntityCount);
    }

    [Fact]
    public async Task PersistentCollisionRollsBackPartiallyCreatedCellAndPreservesExistingOwner(){
        Assert.True(GeneratedWorldCellActivationSink.TryGetPayloadKey(
            "UserEcsProject.Scenes.FirstPersonStageScene",out var PayloadKey));
        var Artifact=WorldCellEntityPayloadCodec.Bake(PayloadKey,new ArtifactProvenance(
            "test","scene://rollback",Hash256.Compute([3]),"generated-scene",Hash256.Compute([4])));
        var Bytes=ContentArtifactCodec.Write(Artifact);
        var Reference=new AssetArtifactReference(PayloadKey,Artifact.ContentHash);
        var Cell=new WorldCellId(StableId.Parse("791ab385-141a-554f-ad23-e908d93ac0bc"));
        var ExistingCell=new WorldCellId(StableId.Parse("997b55fc-6f29-5216-8f8a-3f63dc39704e"));
        var Definition=new WorldCellDefinition(Cell,ReferenceFrameId.Root,
            new FrameLocalBounds(LogicalVector3.Zero,LogicalVector3.One),PayloadKey,ArtifactClosure:[Reference]);
        Assert.True(WorldPartitionManifest.TryCreate([Definition],out var Manifest,out var ManifestError),ManifestError.Message);
        var FramesGraph=new ReferenceFrameGraph(ReferenceFrameId.Root);
        Assert.True(FramesGraph.TryCommit(1,out var Frames,out var FrameError),FrameError.Message);
        using var Owner=new WorldOwner();
        var Collision=new PersistentEntityId(StableId.FromName(Cell.Value,"UserEcsProject.Scenes.FirstPersonStageScene/0"));
        Owner.World.AttachToCell(ExistingCell,Collision,Owner.World.Create(new Bootstrap()));
        await using var Streaming=new WorldCellStreamingService(new WorldCellRuntime(Manifest),
            new SingleArtifactSource(Reference,Bytes),new GeneratedWorldCellActivationSink(Owner));

        var Result=await Streaming.RequestAsync([Cell],Frames,WorldCellStreamingBudget.Unlimited);

        Assert.Equal(WorldCellStreamingErrorCode.ActivationFailed,Result.Error.Code);
        Assert.Empty(Result.Snapshot.ActiveCells);
        Assert.False(Owner.World.HasOwnedCell(Cell));
        Assert.True(Owner.World.HasOwnedCell(ExistingCell));
        Assert.Equal(1,Owner.World.GetStatistics().AliveEntityCount);
        Assert.True(Owner.World.TryResolve(Collision,out _));
    }

    private sealed class SingleArtifactSource(AssetArtifactReference Expected,byte[]Bytes):IWorldCellArtifactSource {
        private int Retains;
        public ValueTask<ReadOnlyMemory<byte>>ReadAsync(AssetArtifactReference Reference,CancellationToken CancellationToken){
            Assert.Equal(Expected,Reference);
            this.Retains++;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(Bytes);
        }
        public ValueTask ReleaseAsync(AssetArtifactReference Reference){
            Assert.Equal(Expected,Reference);
            Assert.True(this.Retains>0);
            this.Retains--;
            return ValueTask.CompletedTask;
        }
    }
}
