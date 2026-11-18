using Xunit;
using System.Numerics;
using CSharpGameEngine.Import.Models;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.Import.Gltf.Tests;
/// <summary>
/// UV0 carries one KHR_texture_transform (the first mapped channel's). A channel with another transform keeps the rest of its mapping
/// relative to UV0, and resource preparation resamples its pixels by it (UE's Manny and Quinn: base colour is shifted, the normal map is not).
/// Before, those channels were sampled with the base colour's mapping and the import warned IMP141.
/// </summary>
public sealed class TextureMappingBakeTests {
    private static readonly GltfUvTransform Identity=new(Vector2.Zero,Vector2.One,0f);
    [Fact]
    public void TheRelativeMappingAppliedAfterUv0GivesTheChannelsOwnMapping(){
        GltfUvTransform[]Chosen=[Identity,new(new(-0.006f,-0.0061f),new(1.0112f,1.0159f),0f),new(new(0.25f,-0.5f),new(2f,0.5f),0f)];
        GltfUvTransform?[]Own=[null,Identity,new(new(-0.0103f,-0.0065f),new(1.0162f,1.0146f),0f),new(new(0.1f,0.2f),new(3f,0.25f),0.7f)];
        Vector2[]Uvs=[new(0f,0f),new(1f,0f),new(0.3f,0.8f),new(-0.4f,1.7f)];
        foreach(var C in Chosen){
            foreach(var T in Own){
                var Relative=BuiltInGltfImporter.RelativeTransform(C,T,IsNormal:false);
                Assert.NotNull(Relative);
                foreach(var Uv in Uvs){
                    var Expected=(T??Identity).Apply(Uv);
                    var Actual=Relative!.Value.Apply(C.Apply(Uv));
                    Assert.True(Vector2.Distance(Expected,Actual)<1e-5f,$"{C} then {Relative} at {Uv}: {Actual}, expected {Expected}");
                }
            }
        }
        Assert.Equal(Identity,BuiltInGltfImporter.RelativeTransform(Chosen[1],Chosen[1],IsNormal:true));
    }
    [Fact]
    public void MappingsThatCannotBeBakedStayUnbaked(){
        var Rotated=new GltfUvTransform(Vector2.Zero,Vector2.One,0.5f);
        Assert.Null(BuiltInGltfImporter.RelativeTransform(Rotated,Identity,IsNormal:false));
        // A normal map must keep its tangent frame: no rotation, no mirroring.
        Assert.Null(BuiltInGltfImporter.RelativeTransform(Identity,Rotated,IsNormal:true));
        Assert.Null(BuiltInGltfImporter.RelativeTransform(Identity,new(Vector2.Zero,new(-1f,1f),0f),IsNormal:true));
        Assert.NotNull(BuiltInGltfImporter.RelativeTransform(Identity,Rotated,IsNormal:false));
    }
    [Fact]
    public void ResamplingShiftsWrapsAndKeepsAnIdentityMapping(){
        const int Size=4;
        var Source=new byte[Size*Size*4];
        for(var Index=0;Index<Size*Size;Index++){ Source[Index*4]=(byte)(Index*10);Source[Index*4+3]=255; }
        Assert.Equal(Source,TextureMappingBake.Resample(Size,Size,Source,new(Vector2.Zero,Vector2.One,0f)));
        // One pixel to the right: the result at x samples the source at x + 1, wrapping at the edge.
        var Shifted=TextureMappingBake.Resample(Size,Size,Source,new(new(1f/Size,0f),Vector2.One,0f));
        for(var Y=0;Y<Size;Y++){
            for(var X=0;X<Size;X++){ Assert.Equal(Source[(Y*Size+(X+1)%Size)*4],Shifted[(Y*Size+X)*4]); }
        }
        Assert.True(TextureMappingBake.IsIdentity(new(Vector2.Zero,Vector2.One,0f)));
        Assert.False(TextureMappingBake.IsIdentity(new(new(1f/Size,0f),Vector2.One,0f)));
    }
}
