using System.Text;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content.Tests;

public sealed class AssetIdentityTests {
    private static readonly AssetId Asset=AssetId.Parse("17b3e569-5a8a-53fd-b317-0d29f9764c39");

    [Fact]
    public void Hash256_RoundTripsCanonicalLowerCase(){
        const string Upper="0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

        Hash256 Hash=Hash256.Parse(Upper);

        Assert.True(Hash.IsValid);
        Assert.Equal(Upper.ToLowerInvariant(),Hash.ToString());
        Assert.Equal(Hash,Hash256.FromBytes(Hash.ToByteArray()));
        Assert.False(Hash256.TryParse("1234",out _));
    }

    [Fact]
    public void AssetUri_RoundTripsAssetAndSubAssetIdentity(){
        SubAssetId Sub=SubAssetId.FromCanonicalBytes(Encoding.UTF8.GetBytes("mesh:body"));
        AssetUri Expected=new(new AssetKey(Asset,Sub));

        Assert.True(AssetUri.TryParse(Expected.ToString().ToUpperInvariant().Replace("ASSET://","asset://"),out var Parsed,out var Error),Error);
        Assert.Equal(Expected,Parsed);
        Assert.Equal("asset://"+Asset+"/"+Sub,Parsed.ToString());
        Assert.False(AssetUri.TryParse("asset://"+Asset+"/",out _,out _));
        Assert.False(AssetUri.TryParse("http://"+Asset,out _,out _));
    }

    [Fact]
    public void Metadata_UsesStableFieldAndOptionOrder(){
        AssetMetadata Metadata=AssetMetadata.Create(
            Asset,
            @"Assets\Models\Ship.glb",
            "SharpGLTF/1.0.6",
            [new("z-option","last"),new("a-option","first")]);

        byte[]First=Metadata.ToCanonicalUtf8();
        byte[]Second=Metadata.ToCanonicalUtf8();
        string Json=Encoding.UTF8.GetString(First);

        Assert.Equal(First,Second);
        Assert.Equal(
            """{"schemaVersion":1,"assetId":"17b3e569-5a8a-53fd-b317-0d29f9764c39","sourceUri":"Assets/Models/Ship.glb","importer":"SharpGLTF/1.0.6","options":{"a-option":"first","z-option":"last"}}""",
            Json);
        Assert.True(AssetMetadata.TryParse(First,out var Parsed,out var Error),Error.Message);
        Assert.Equal("first",Parsed!.Options["a-option"]);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"schemaVersion":1,"assetId":"17b3e569-5a8a-53fd-b317-0d29f9764c39","sourceUri":"a","importer":"i","options":{}}""",AssetMetadataErrorCode.DuplicateField)]
    [InlineData("""{"schemaVersion":2,"assetId":"17b3e569-5a8a-53fd-b317-0d29f9764c39","sourceUri":"a","importer":"i","options":{}}""",AssetMetadataErrorCode.UnsupportedSchema)]
    [InlineData("""{"schemaVersion":1,"assetId":"17b3e569-5a8a-53fd-b317-0d29f9764c39","sourceUri":"a","importer":"i","options":{"x":"1","x":"2"}}""",AssetMetadataErrorCode.DuplicateField)]
    public void Metadata_RejectsNonCanonicalOrStaleInput(string Json,AssetMetadataErrorCode Expected){
        Assert.False(AssetMetadata.TryParse(Encoding.UTF8.GetBytes(Json),out _,out var Error));
        Assert.Equal(Expected,Error.Code);
    }

    [Fact]
    public void Metadata_RejectsSemanticallyValidButNonCanonicalEncoding(){
        string Json="""
        { "assetId": "17b3e569-5a8a-53fd-b317-0d29f9764c39", "schemaVersion": 1, "sourceUri": "Assets/Ship.glb", "importer": "SharpGLTF", "options": {} }
        """;

        Assert.False(AssetMetadata.TryParse(Encoding.UTF8.GetBytes(Json),out _,out AssetMetadataError Error));
        Assert.Equal("canonical",Error.Field);
    }
}
