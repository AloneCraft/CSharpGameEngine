using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.World.Tests;

public sealed class WorldCellEntityPayloadTests {
    [Fact]
    public void GeneratedSceneIdentityRoundTripsThroughCanonicalArtifact(){
        var Key=new AssetKey(new AssetId(StableId.Parse("738f78ad-69aa-59b6-9f17-574780bfe93f")),null);
        var Artifact=WorldCellEntityPayloadCodec.Bake(Key,Provenance());
        var Bytes=ContentArtifactCodec.Write(Artifact);

        Assert.True(ContentArtifactCodec.TryRead(Bytes,out var Read,out var EnvelopeError),EnvelopeError.Message);
        Assert.True(WorldCellEntityPayloadCodec.TryRead(Read!,out var Actual,out var Error),Error);
        Assert.Equal(Key,Actual);
    }

    [Fact]
    public void WrongArtifactKindIsRejectedBeforeDispatch(){
        var Artifact=new ContentArtifact(ContentArtifactKind.Mesh,Provenance(),null,[1,2,3]);

        Assert.False(WorldCellEntityPayloadCodec.TryRead(Artifact,out _,out var Error));
        Assert.Contains("Scene",Error,StringComparison.Ordinal);
    }

    private static ArtifactProvenance Provenance()=>new(
        "test","scene://generated",Hash256.Compute([1]),"generated-scene",Hash256.Compute([2]));
}
