using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Tests;
public sealed class ModelArtifactTests {
    private static ModelMeshData Triangle()=>new("triangle",[Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],[new(1f,0f,0f,1f),new(1f,0f,0f,1f),new(1f,0f,0f,1f)],[[Vector2.Zero,Vector2.UnitX,Vector2.UnitY]],[],[0,1,2],[new(0,3,-1)],[],null,[],[],new(Vector3.Zero,Vector3.One));
    [Fact]
    public void Mesh_RoundTripsWithCanonicalLittleEndianValuesAndRejectsWrongKind(){
        var Input=Triangle();
        var Bytes=ModelArtifactCodec.WriteMesh(Input);
        var Result=ModelArtifactCodec.ReadMesh(Bytes);
        Assert.Equal(Input.Positions.ToArray(),Result.Positions.ToArray());
        Assert.Equal(Bytes,ModelArtifactCodec.WriteMesh(Result));
        Assert.Throws<InvalidDataException>(()=>ModelArtifactCodec.ReadModel(Bytes));
        Assert.Throws<InvalidDataException>(()=>ModelArtifactCodec.ReadMesh(Bytes.AsSpan(0,Bytes.Length-1)));
        Assert.Throws<InvalidDataException>(()=>ModelArtifactCodec.WriteMesh(Input with { Indices=[0,1,99] }));
    }
    [Fact]
    public void SkeletonClipAndLicense_AreRuntimeOnlyPayloads(){
        var Rest=new ModelLocalTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);
        var Skeleton=new ModelSkeletonData("rig",["root"],[-1],[Rest],[1],Matrix4x4.CreateTranslation(1f,2f,3f));
        var Decoded=ModelArtifactCodec.ReadSkeleton(ModelArtifactCodec.WriteSkeleton(Skeleton));
        Assert.Equal(Skeleton.RootTransform,Decoded.RootTransform);
        var Keys=new ModelKeys<Vector3>([0f,1f],[Vector3.Zero,Vector3.UnitX],ModelInterpolation.Linear,[],[]);
        var Clip=new ModelAnimationClipData("walk",0f,1f,30f,0,[new("root",0,Keys,null,null)],[],[],[],null);
        Assert.Equal(Vector3.UnitX,ModelArtifactCodec.ReadClip(ModelArtifactCodec.WriteClip(Clip)).Tracks[0].Translation!.Values[1]);
        var License=new ModelLicenseData("CC0",true,"author","https://example.invalid",[]);
        Assert.Equal(License.Kind,ModelArtifactCodec.ReadLicense(ModelArtifactCodec.WriteLicense(License)).Kind);
        Assert.Throws<InvalidDataException>(()=>ModelArtifactCodec.WriteSkeleton(Skeleton with { ParentIndices=[0] }));
    }
    [Fact]
    public void ModelReferences_AreExactAndDoNotRequireSourceFiles(){
        var Key=new AssetKey(AssetId.Parse("00000000-0000-5000-8000-000000000001"),SubAssetIdentity.Derive(SubAssetKind.Mesh,"triangle"));
        var Reference=new AssetArtifactReference(Key,Hash256.Compute([1,2,3]));
        var Model=new ModelData("model",[new("root",-1,new(Vector3.Zero,Quaternion.Identity,Vector3.One),0,[],-1,0)],[ModelReference.From(Reference)],[],[],[],[],[],new(Int128.MaxValue,Int128.MinValue,1));
        var Result=ModelArtifactCodec.ReadModel(ModelArtifactCodec.WriteModel(Model));
        Assert.Equal(Reference,Result.Meshes[0].ToReference());
        Assert.Equal(Model.Origin,Result.Origin);
    }
    [Theory]
    [InlineData(ContentArtifactKind.Model)]
    [InlineData(ContentArtifactKind.Skeleton)]
    [InlineData(ContentArtifactKind.AnimationClip)]
    [InlineData(ContentArtifactKind.DistanceField)]
    [InlineData(ContentArtifactKind.License)]
    public void NewKinds_RoundTripTheExistingArtifactEnvelope(ContentArtifactKind Kind){
        var Provenance=new ArtifactProvenance("test","Assets/model.glb",Hash256.Compute([1]),"test",Hash256.Compute([2]));
        var Bytes=ContentArtifactCodec.Write(new(Kind,Provenance,[],[1,2,3]));
        Assert.True(ContentArtifactCodec.TryRead(Bytes,out var Result,out var Error),Error.ToString());
        Assert.Equal(Kind,Result!.Kind);
    }
    [Fact]
    public void RuntimeModelAssembly_DoesNotReferenceCookingOrImport(){
        var References=typeof(ModelData).Assembly.GetReferencedAssemblies().Select(Reference=>Reference.Name).ToArray();
        Assert.DoesNotContain("CSharpGameEngine.Cooking",References);
        Assert.DoesNotContain("CSharpGameEngine.Import",References);
    }
}
