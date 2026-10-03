using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.World;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Runtime.Lighting;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// Audit AUD-001: the typed light Components are persistent, so a snapshot of a loaded cell must restore them. Their
/// physical values are read-only structs that validate their range.
/// </summary>
public sealed class LightComponentSnapshotTests {
    [Fact]
    public void LightComponents_RoundTripThroughTheSnapshotCodec(){
        AssertRoundTrip(new DirectionalLight { Color=new LinearLightColor(1f,0.9f,0.8f),Illuminance=new Illuminance(120_000f) });
        AssertRoundTrip(new PointLight { Color=new LinearLightColor(0.2f,0.4f,1f),Flux=new LuminousFlux(1500f),Range=new LightRange(25f) });
        AssertRoundTrip(new SpotLight { Flux=new LuminousFlux(600f),Range=new LightRange(12f),Cone=new SpotConeAngles(20f,35f) });
        AssertRoundTrip(new RectAreaLight { Luminance=new Luminance(250f),Size=new RectangleSize(2f,0.5f),Range=new LightRange(8f) });
        AssertRoundTrip(new LightShadow { Priority=3,Bias=new ShadowBias(0.01f) });
    }

    [Fact]
    public void LightComponents_RejectAPayloadOutsideTheirRange(){
        // The constructor's range check runs on restore: a tampered negative flux is a codec error, not a light.
        var Payload=System.Text.Encoding.UTF8.GetBytes("""{"Color":{"B":1,"G":1,"R":1},"Flux":{"Lumens":-5},"Range":{"Meters":10}}""");
        Assert.False(EcsSnapshotComponentCodec<PointLight>.TryDeserialize(Payload,out _,out var Error));
        Assert.Contains("LuminousFlux",Error);
    }

    [Fact]
    public async Task ACellHoldingLights_RestoresRightAfterItsCapture(){
        // The audit's world case: a streamed cell whose entities hold lights and shadows is captured and restored at once.
        Assert.True(GeneratedWorldCellActivationSink.TryGetPayloadKey("UserEcsProject.Scenes.EngineShowcaseScene",out var PayloadKey));
        var Artifact=WorldCellEntityPayloadCodec.Bake(PayloadKey,new ArtifactProvenance(
            "test","scene://lights",Hash256.Compute([5]),"generated-scene",Hash256.Compute([6])));
        var Reference=new AssetArtifactReference(PayloadKey,Artifact.ContentHash);
        var Cell=new WorldCellId(StableId.Parse("6c1c8a4e-2f0e-5d33-9a4e-6f3f6d1a2b77"));
        Assert.True(WorldPartitionManifest.TryCreate([new WorldCellDefinition(Cell,ReferenceFrameId.Root,
            new FrameLocalBounds(LogicalVector3.Zero,LogicalVector3.One),PayloadKey,ArtifactClosure:[Reference])],
            out var Manifest,out var ManifestError),ManifestError.Message);
        var FramesGraph=new ReferenceFrameGraph(ReferenceFrameId.Root);
        Assert.True(FramesGraph.TryCommit(1,out var Frames,out var FrameError),FrameError.Message);
        using var Owner=new WorldOwner();
        await using var Streaming=new WorldCellStreamingService(new WorldCellRuntime(Manifest),
            new SingleArtifactSource(ContentArtifactCodec.Write(Artifact)),new GeneratedWorldCellActivationSink(Owner));
        var Active=await Streaming.RequestAsync([Cell],Frames,WorldCellStreamingBudget.Unlimited);
        Assert.True(Active.Succeeded,Active.Error.Message);
        byte[]Snapshot=Owner.World.CaptureSnapshot(Frames.Tick,Hash256.Compute([7]).ToString());
        Assert.True(EcsSnapshotArtifactCodec.TryRead(Snapshot,global::CSharpGameEngine.Entities.World.SnapshotSchemaFingerprint,out var Document,out var ReadError),ReadError.Message);
        Assert.Contains(Document!.Entities,Entity=>Entity.Components.Any(static Component=>Component.TypeId==typeof(LightShadow).FullName));

        EcsSnapshotRestoreResult Restore=Owner.World.RestoreSnapshot(Snapshot);

        Assert.True(Restore.Succeeded,Restore.Error.Message);
    }

    private sealed class SingleArtifactSource(byte[]Bytes):IWorldCellArtifactSource {
        public ValueTask<ReadOnlyMemory<byte>>ReadAsync(AssetArtifactReference Reference,CancellationToken CancellationToken)=>
            ValueTask.FromResult<ReadOnlyMemory<byte>>(Bytes);
        public ValueTask ReleaseAsync(AssetArtifactReference Reference)=>ValueTask.CompletedTask;
    }

    private static void AssertRoundTrip<T>(T Value)where T:struct{
        Assert.False(EcsSnapshotComponentCodec<T>.IsTransient);
        var Payload=EcsSnapshotComponentCodec<T>.Serialize(Value);
        Assert.True(EcsSnapshotComponentCodec<T>.TryDeserialize(Payload,out var Restored,out var Error),$"{typeof(T).Name}: {Error} ({System.Text.Encoding.UTF8.GetString(Payload)})");
        Assert.Equal(Value,Restored);
    }
}
