using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.World.Tests;

public sealed class WorldCellArtifactTests {
    private static readonly StableId Namespace=StableId.Parse("d4f14467-ae64-521f-a94b-f45716627020");

    [Fact]
    public void Bake_IsCanonicalAndRoundTripsQ64PointBoundsAndClosure(){
        WorldCellDefinition Definition=CreateDefinition();
        ArtifactProvenance Provenance=CreateProvenance();

        byte[]First=ContentArtifactCodec.Write(WorldCellArtifactCodec.Bake(Definition,Provenance));
        byte[]Second=ContentArtifactCodec.Write(WorldCellArtifactCodec.Bake(Definition,Provenance));

        Assert.Equal(First,Second);
        Assert.True(WorldCellArtifactCodec.TryRead(First,out var Read,out var Error),Error.Message);
        Assert.NotNull(Read);
        Assert.Equal(Definition.Id,Read!.Id);
        Assert.Equal(Definition.Frame,Read.Frame);
        Assert.Equal(Definition.Bounds,Read.Bounds);
        Assert.Equal(Definition.EntityPayload,Read.EntityPayload);
        Assert.Equal(Definition.LevelOfDetail,Read.LevelOfDetail);
        Assert.Equal(
            Definition.Dependencies.OrderBy(static Id=>Id.ToString(),StringComparer.Ordinal),
            Read.Dependencies);
        Assert.Equal(2,Read.ArtifactClosure.Length);
    }

    [Fact]
    public void Read_RejectsCorruptEnvelopeWithoutReturningDefinition(){
        byte[]Bytes=ContentArtifactCodec.Write(WorldCellArtifactCodec.Bake(CreateDefinition(),CreateProvenance()));
        Bytes[^1]^=0x01;

        Assert.False(WorldCellArtifactCodec.TryRead(Bytes,out var Definition,out var Error));
        Assert.Null(Definition);
        Assert.Equal(WorldCellArtifactErrorCode.InvalidEnvelope,Error.Code);
    }

    [Fact]
    public void Manifest_RejectsClosureThatOmitsEntityPayload(){
        WorldCellDefinition Valid=CreateDefinition();
        WorldCellDefinition Missing=new(
            Valid.Id,Valid.Frame,Valid.Bounds,Valid.EntityPayload,Valid.LevelOfDetail,
            Valid.Dependencies,[]);

        Assert.False(WorldPartitionManifest.TryCreate([Missing],out _,out var Error));
        Assert.Equal(WorldPartitionErrorCode.MissingEntityPayloadArtifact,Error.Code);
    }

    private static WorldCellDefinition CreateDefinition(){
        WorldCellId Cell=new(StableId.FromName(Namespace,"cell"));
        ReferenceFrameId Frame=new(StableId.FromName(Namespace,"planet"));
        AssetKey Payload=new(new AssetId(StableId.FromName(Namespace,"entities")),null);
        AssetKey Material=new(
            new AssetId(StableId.FromName(Namespace,"material")),
            SubAssetId.FromCanonicalBytes(Encoding.UTF8.GetBytes("material:surface")));
        AssetArtifactReference EntityReference=new(
            Payload,Hash256.Compute(Encoding.UTF8.GetBytes("entity payload")));
        AssetArtifactReference MaterialReference=new(
            Material,Hash256.Compute(Encoding.UTF8.GetBytes("material payload")));
        return new WorldCellDefinition(
            Cell,Frame,
            new FrameLocalBounds(
                new LogicalVector3(LogicalScalar.ParseInvariant("0.125"),2,3),
                new LogicalVector3(100,200,300)),
            Payload,2,
            [new WorldCellId(StableId.FromName(Namespace,"z")),new WorldCellId(StableId.FromName(Namespace,"a"))],
            [MaterialReference,EntityReference]);
    }

    private static ArtifactProvenance CreateProvenance()=>new(
        "cge-world-bake/1",
        "world://solar-system/earth",
        Hash256.Compute(Encoding.UTF8.GetBytes("world source")),
        "CSharpWorldBake",
        Hash256.Compute(Encoding.UTF8.GetBytes("world options")));
}
