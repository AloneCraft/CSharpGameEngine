using Xunit;
using System.Text;
using System.Buffers.Binary;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.World.Tests;

public sealed class WorldCellArtifactTests {
    private static readonly StableId Namespace=StableId.Parse("d4f14467-ae64-521f-a94b-f45716627020");

    [Fact]
    public void Bake_IsCanonicalAndRoundTripsNumericValuesBoundsAndClosure(){
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
    [Fact]
    public void Read_RejectsTheVersionOneQ64Payload(){
        var Current=WorldCellArtifactCodec.Bake(CreateDefinition(),CreateProvenance());
        var Source=Current.Payload.AsSpan();var Tail=84;
        for(var Index=0;Index<6;Index++){ Tail+=2+BinaryPrimitives.ReadUInt16LittleEndian(Source.Slice(Tail,2)); }
        var Legacy=new byte[253+Current.Payload.Length-Tail-73];Source[..84].CopyTo(Legacy);
        BinaryPrimitives.WriteUInt16LittleEndian(Legacy.AsSpan(4),1);
        var Values=new Int128[]{Int128.One<<61,(Int128)2<<64,(Int128)3<<64,(Int128)100<<64,(Int128)200<<64,(Int128)300<<64};
        for(var Index=0;Index<Values.Length;Index++){ BinaryPrimitives.WriteInt128LittleEndian(Legacy.AsSpan(84+Index*16),Values[Index]); }
        Source[Tail..].CopyTo(Legacy.AsSpan(180));
        var Envelope=ContentArtifactCodec.Write(new ContentArtifact(Current.Kind,Current.Provenance,Current.Dependencies,Legacy));
        // ADR-0098 §5 の C：WorldCell はベイクで作り直せる生成物なので、版 1 は読まずに拒否する。
        Assert.False(WorldCellArtifactCodec.TryRead(Envelope,out var Read,out var Error));Assert.Null(Read);Assert.Equal(WorldCellArtifactErrorCode.InvalidPayload,Error.Code);
    }
    [Theory]
    [InlineData("rounded")]
    [InlineData("overflow")]
    [InlineData("malformed")]
    public void Read_ConvertsCommonValuesOnceAndRejectsInvalidDataAtomically(string Case){
        var Current=WorldCellArtifactCodec.Bake(CreateDefinition(),CreateProvenance());
        var Raw=new byte[Case=="overflow"?24:16];
        if(Case=="overflow"){ Raw[16]=1; }else{ Raw[0]=3; }
        var Value=NumericValueCodec.WriteFixed(Raw,Case=="overflow"?0:65);
        if(Case=="malformed"){ Value[0]^=1; }
        var PreviousLength=BinaryPrimitives.ReadUInt16LittleEndian(Current.Payload.AsSpan().Slice(84,2));
        var Payload=new byte[Current.Payload.Length-PreviousLength+Value.Length];Current.Payload.AsSpan()[..84].CopyTo(Payload);
        BinaryPrimitives.WriteUInt16LittleEndian(Payload.AsSpan(84),checked((ushort)Value.Length));Value.CopyTo(Payload.AsSpan(86));
        Current.Payload.AsSpan()[(86+PreviousLength)..].CopyTo(Payload.AsSpan(86+Value.Length));
        var Envelope=ContentArtifactCodec.Write(new ContentArtifact(Current.Kind,Current.Provenance,Current.Dependencies,Payload));
        var Success=WorldCellArtifactCodec.TryRead(Envelope,out var Read,out var Error);
        if(Case=="rounded"){ Assert.True(Success,Error.Message);Assert.Equal(LogicalScalar.FromRaw(2),Read!.Bounds.Center.R); }else{ Assert.False(Success);Assert.Null(Read);Assert.Equal(WorldCellArtifactErrorCode.InvalidBounds,Error.Code); }
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
