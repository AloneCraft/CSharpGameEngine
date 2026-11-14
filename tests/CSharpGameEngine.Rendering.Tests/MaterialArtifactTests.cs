using System.Numerics;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Rendering.Tests;

public sealed class MaterialArtifactTests {
    private static readonly StableId Namespace=
        StableId.Parse("486a9a55-b8ca-57ce-a040-9127418144e6");
    private static readonly ArtifactProvenance Provenance=new(
        "test-1","asset://material-test",
        Hash256.Compute(Encoding.UTF8.GetBytes("source")),
        "test",Hash256.Compute(Encoding.UTF8.GetBytes("options")));

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void Catalog_ValidatesDependenciesDeduplicatesResidencyAndShadesCpuReference(bool IncludeGrid,bool AnchorToObject){
        RenderMaterialCatalog Catalog=new();
        AssetKey RedKey=Key("red");
        AssetKey RedAliasKey=Key("red-alias");
        AssetKey NormalKey=Key("normal");
        byte[]RedEnvelope=RenderArtifactCodec.CreateTextureEnvelope(
            Provenance,2,1,[255,0,0,255,0,255,0,255]);
        RenderTextureResource Red=Catalog.RegisterTexture(RedKey,RedEnvelope);
        RenderTextureResource RedAlias=Catalog.RegisterTexture(RedAliasKey,RedEnvelope);
        RenderTextureResource Normal=Catalog.RegisterTexture(
            NormalKey,RenderArtifactCodec.CreateTextureEnvelope(
                Provenance,1,1,[128,128,255,255]));
        AssetArtifactReference RedReference=new(RedKey,Red.ContentHash);
        AssetArtifactReference NormalReference=new(NormalKey,Normal.ContentHash);
        RenderMaterialArtifactDefinition Definition=new(
            new Vector4(0.5f,1f,1f,1f),0.75f,0.25f,new Vector3(10f,20f,30f),1f,
            RedReference,NormalReference,RedReference,RedReference,IncludeGrid?new(1f,10,0.02f,new(0.2f),new(0.4f),AnchorToObject):null);
        AssetKey MaterialKey=Key("material");

        RenderMaterialResource Material=Catalog.RegisterMaterial(
            MaterialKey,RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Definition));

        Assert.Same(Red,RedAlias);
        Assert.Equal(3,Catalog.TextureAssetCount);
        Assert.Equal(2,Catalog.ResidentTextureCount);
        Assert.Equal(1,Catalog.MaterialCount);
        Assert.Equal(Definition.WorldGrid,Material.WorldGrid);
        Assert.Equal(new Vector4(0.5f,0f,0f,1f),Material.ShadeBaseColor(0f,0f));
        Assert.Equal(new Vector4(0f,1f,0f,1f),Material.ShadeBaseColor(1f,0f));
        Assert.True(Catalog.TryGetMaterial(MaterialKey,out RenderMaterialResource?Resolved));
        Assert.Same(Material,Resolved);
    }

    [Fact]
    public void MaterialRegistrationIsAtomicWhenDependencyIsMissingOrHashMismatches(){
        RenderMaterialCatalog Catalog=new();
        AssetKey TextureKey=Key("texture");
        RenderTextureResource Texture=Catalog.RegisterTexture(
            TextureKey,RenderArtifactCodec.CreateTextureEnvelope(
                Provenance,1,1,[255,255,255,255]));
        AssetArtifactReference Valid=new(TextureKey,Texture.ContentHash);
        AssetArtifactReference Missing=new(
            Key("missing"),Hash256.Compute(Encoding.UTF8.GetBytes("missing")));
        RenderMaterialArtifactDefinition MissingDefinition=new(
            Vector4.One,1f,0f,Vector3.Zero,1f,Missing,Valid,Valid,Valid);

        Assert.Throws<InvalidDataException>(()=>Catalog.RegisterMaterial(
            Key("missing-material"),
            RenderArtifactCodec.CreateMaterialEnvelope(Provenance,MissingDefinition)));
        Assert.Equal(0,Catalog.MaterialCount);

        AssetArtifactReference Mismatch=new(
            TextureKey,Hash256.Compute(Encoding.UTF8.GetBytes("other")));
        RenderMaterialArtifactDefinition MismatchDefinition=new(
            Vector4.One,1f,0f,Vector3.Zero,1f,Mismatch,Valid,Valid,Valid);
        Assert.Throws<ArgumentException>(()=>
            RenderArtifactCodec.CreateMaterialEnvelope(Provenance,MismatchDefinition));
        Assert.Equal(0,Catalog.MaterialCount);
    }

    [Fact]
    public void CorruptEnvelopeAndInvalidMaterialNeverReplaceLastKnownGood(){
        RenderMaterialCatalog Catalog=new();
        AssetKey TextureKey=Key("stable");
        byte[]Envelope=RenderArtifactCodec.CreateTextureEnvelope(
            Provenance,1,1,[1,2,3,255]);
        RenderTextureResource First=Catalog.RegisterTexture(TextureKey,Envelope);
        byte[]Corrupt=[.. Envelope];
        Corrupt[^1]^=0xff;

        Assert.Throws<InvalidDataException>(()=>Catalog.RegisterTexture(TextureKey,Corrupt));
        Assert.Same(First,Catalog.RegisterTexture(TextureKey,Envelope));
        Assert.Equal(1,Catalog.TextureAssetCount);
    }

    [Fact]
    public void ArtifactCodecRejectsNonFiniteAndDimensionMismatch(){
        AssetArtifactReference Reference=new(
            Key("texture"),Hash256.Compute(Encoding.UTF8.GetBytes("texture")));
        RenderMaterialArtifactDefinition Invalid=new(
            new Vector4(float.NaN,1f,1f,1f),1f,0f,Vector3.Zero,1f,
            Reference,Reference,Reference,Reference);

        Assert.Throws<ArgumentException>(()=>
            RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Invalid));
        Assert.Throws<ArgumentException>(()=>
            RenderArtifactCodec.CreateTextureEnvelope(Provenance,2,1,[0,0,0,0]));
        foreach(var Grid in new StageWorldGrid[]{new(float.NaN,10,0.02f,Vector3.One,Vector3.One),new(1f,0,0.02f,Vector3.One,Vector3.One),new(1f,10,0.5f,Vector3.One,Vector3.One),new(1f,10,0.02f,new(-1f),Vector3.One)}){
            Assert.ThrowsAny<ArgumentException>(()=>RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Invalid with {BaseColorFactor=Vector4.One,WorldGrid=Grid}));
        }
    }
    [Theory]
    [InlineData("truncated")]
    [InlineData("flags")]
    [InlineData("nan")]
    public void MalformedWorldGridMaterialCannotChangeTheCatalog(string Damage){
        var Catalog=new RenderMaterialCatalog();var TextureKey=Key("grid-texture");
        var Texture=Catalog.RegisterTexture(TextureKey,RenderArtifactCodec.CreateTextureEnvelope(Provenance,1,1,[255,255,255,255]));
        var Reference=new AssetArtifactReference(TextureKey,Texture.ContentHash);
        var Definition=new RenderMaterialArtifactDefinition(Vector4.One,1f,0f,Vector3.Zero,1f,Reference,Reference,Reference,Reference,new(1f,10,0.02f,Vector3.One,Vector3.One));
        var Envelope=RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Definition);
        Assert.True(ContentArtifactCodec.TryRead(Envelope,out var Artifact,out _));
        var Payload=Artifact!.Payload.ToArray();
        if(Damage=="truncated"){ Payload=Payload[..^1]; }
        else if(Damage=="flags"){ Payload[6]=0; }
        else{ System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(Payload.AsSpan(48,4),float.NaN); }
        var Broken=ContentArtifactCodec.Write(new(ContentArtifactKind.Material,Provenance,Artifact.Dependencies,Payload));
        Assert.Throws<InvalidDataException>(()=>Catalog.RegisterMaterial(Key("grid-material"),Broken));
        Assert.Equal(0,Catalog.MaterialCount);
        Assert.Equal(Definition.WorldGrid,Catalog.RegisterMaterial(Key("grid-material"),Envelope).WorldGrid);
    }

    [Fact]
    public void MaterialPayloadCanonicalizesNegativeZero(){
        AssetArtifactReference Reference=new(
            Key("zero-texture"),Hash256.Compute(Encoding.UTF8.GetBytes("texture")));
        RenderMaterialArtifactDefinition Positive=new(
            new Vector4(0f,1f,1f,1f),1f,0f,Vector3.Zero,1f,
            Reference,Reference,Reference,Reference,new(1f,10,0.02f,new(0f,1f,0f),Vector3.Zero));
        RenderMaterialArtifactDefinition Negative=new(
            new Vector4(-0f,1f,1f,1f),1f,-0f,new Vector3(-0f,0f,-0f),1f,
            Reference,Reference,Reference,Reference,new(1f,10,0.02f,new(-0f,1f,-0f),new(-0f,0f,-0f)));

        Assert.Equal(
            RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Positive),
            RenderArtifactCodec.CreateMaterialEnvelope(Provenance,Negative));
    }

    private static AssetKey Key(string Name)=>new(
        new AssetId(StableId.FromName(Namespace,Name)),null);
}
