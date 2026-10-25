using System.Text;
namespace CSharpGameEngine.Contracts.Tests;
public sealed class ModelIdentityTests {
    [Fact]
    public void Identity_IsVersionedUtf8NfcAndKindSensitive(){
        var Expected=SubAssetId.FromCanonicalBytes(Encoding.UTF8.GetBytes("cge.subasset/1\nmesh\nCaf\u00e9"));
        Assert.Equal(Expected,SubAssetIdentity.Derive(SubAssetKind.Mesh,"Cafe\u0301"));
        Assert.NotEqual(Expected,SubAssetIdentity.Derive(SubAssetKind.Clip,"Caf\u00e9"));
        Assert.NotEqual(Expected,SubAssetIdentity.Derive(SubAssetKind.Mesh,"caf\u00e9"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>SubAssetIdentity.Derive((SubAssetKind)255,"name"));
        Assert.Throws<ArgumentException>(()=>SubAssetIdentity.Derive(SubAssetKind.Mesh,""));
        Assert.True(SubAssetIdentity.Derive(SubAssetKind.License,"").IsValid);
    }
    [Theory]
    [InlineData("Assets/日本 #;.glb","root/Walk:100%")]
    [InlineData("C:/Games/Assets/Hero.fbx","Walk_Fwd")]
    public void Selector_RoundTripsNamesAndPreservesSourcePath(string Path,string Name){
        var Selector=new AssetSelector(Path,SubAssetKind.Clip,Name);
        var Parsed=AssetSelector.Parse(Selector.ToString(),SubAssetKind.Clip);
        Assert.Equal(Path,Parsed.Path);
        Assert.Equal(Name,Parsed.Name);
        Assert.Equal(SubAssetKind.Clip,Parsed.Kind);
        Assert.DoesNotContain("root/",Selector.ToString());
    }
    [Fact]
    public void Selector_DoesNotTreatDriveAsSelectorAndRejectsBrokenEscapes(){
        Assert.Null(AssetSelector.Parse("C:\\Assets\\Hero.fbx",SubAssetKind.Mesh).Name);
        Assert.Null(AssetSelector.Parse("Assets/日本 #;.glb",SubAssetKind.Mesh).Name);
        Assert.Throws<FormatException>(()=>AssetSelector.Parse("Assets/Hero.fbx:Walk%2",SubAssetKind.Clip));
        Assert.Throws<FormatException>(()=>AssetSelector.Parse("Assets/Hero.fbx:",SubAssetKind.Clip));
    }
    [Fact]
    public void Humanoid_ExposesFiftyFiveBonesAndFifteenImmutableRequirements(){
        Assert.Equal(55,Enum.GetValues<HumanoidBone>().Length);
        Assert.Equal(15,HumanoidBones.Required.Count);
        Assert.Equal(15,HumanoidBones.Required.Distinct().Count());
        Assert.Contains(HumanoidBone.Hips,HumanoidBones.Required);
        Assert.DoesNotContain(HumanoidBone.UpperChest,HumanoidBones.Required);
    }
}
