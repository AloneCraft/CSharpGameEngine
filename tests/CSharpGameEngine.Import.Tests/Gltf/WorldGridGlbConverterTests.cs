using System.Numerics;
using CSharpGameEngine.Unreal.SceneImporter;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
namespace CSharpGameEngine.Import.Gltf.Tests;
/// <summary>
/// M0098: UnrealのM_PrototypeGrid系マテリアルの焼き付けglbを、ジオメトリを変えずにワールドグリッドのマテリアルへ書き換える。
/// </summary>
public sealed class WorldGridGlbConverterTests {
    private const string GridMaterial="/Game/LevelPrototyping/Materials/MI_PrototypeGrid_Gray.MI_PrototypeGrid_Gray";
    [Fact]
    public void ConvertAll_BakedGridMaterial_BecomesAWorldGridWithTheSameGeometry(){
        using var Directory=new TemporaryDirectory();
        var AssetPath="Meshes/Floor.glb";
        WriteBakedFloor(Path.Combine(Directory.Path,AssetPath));
        var Before=GltfImporter.ImportScene(Path.Combine(Directory.Path,AssetPath)).Scene!;
        Assert.NotNull(Before.Materials[0].BaseColorTextureBytes);
        var Converted=WorldGridGlbConverter.ConvertAll(CreateManifest(AssetPath,GridMaterial),Directory.Path);
        var Result=GltfImporter.ImportScene(Path.Combine(Directory.Path,AssetPath));
        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        var After=Result.Scene!;
        Assert.Equal(1,Converted);
        Assert.DoesNotContain(Result.Diagnostics,Item=>Item.Message.Contains("TEXCOORD_",StringComparison.Ordinal));
        var Material=Assert.Single(After.Materials);
        Assert.Null(Material.BaseColorTextureBytes);
        Assert.Equal(new Vector4(0.18f,0.18f,0.18f,1f),Material.BaseColorFactor);
        Assert.Equal(0f,Material.MetallicFactor);
        Assert.Equal(1f,Material.RoughnessFactor);
        var Grid=Assert.IsType<ImportedWorldGrid>(Material.WorldGrid);
        Assert.Equal(1f,Grid.GridSizeMeters,1e-6f);
        Assert.Equal(5,Grid.SubGridDivisions);
        Assert.Equal(5f/512f,Grid.WireHalfWidth,1e-7f);
        Assert.Equal(new Vector3(0.026f),Grid.GridColor);
        Assert.Equal(new Vector3(0.088f),Grid.SubGridColor);
        var BeforeMesh=Assert.Single(Before.Meshes);
        var AfterMesh=Assert.Single(After.Meshes);
        Assert.Equal(BeforeMesh.Positions,AfterMesh.Positions);
        Assert.Equal(BeforeMesh.Normals,AfterMesh.Normals);
        Assert.Equal(BeforeMesh.Indices,AfterMesh.Indices);
        Assert.Equal(Before.Nodes[0].WorldTransform,After.Nodes[0].WorldTransform);
    }
    [Fact]
    public void ConvertAll_AlreadyConvertedAsset_IsRewrittenToTheSameBytes(){
        using var Directory=new TemporaryDirectory();
        var AssetPath="Floor.glb";
        WriteBakedFloor(Path.Combine(Directory.Path,AssetPath));
        var Manifest=CreateManifest(AssetPath,GridMaterial);
        WorldGridGlbConverter.ConvertAll(Manifest,Directory.Path);
        var First=File.ReadAllBytes(Path.Combine(Directory.Path,AssetPath));
        WorldGridGlbConverter.ConvertAll(Manifest,Directory.Path);
        Assert.Equal(First,File.ReadAllBytes(Path.Combine(Directory.Path,AssetPath)));
    }
    [Fact]
    public void ConvertAll_VariantWithoutAGridMaterial_IsLeftUntouched(){
        using var Directory=new TemporaryDirectory();
        var AssetPath="Colored.glb";
        WriteBakedFloor(Path.Combine(Directory.Path,AssetPath));
        var Before=File.ReadAllBytes(Path.Combine(Directory.Path,AssetPath));
        var Manifest=CreateManifest(AssetPath,"/Game/ThirdPerson/MI_ThirdPersonColWay.MI_ThirdPersonColWay");
        Manifest.MaterialGrids[GridMaterial]=CreateParameters();
        Assert.Equal(0,WorldGridGlbConverter.ConvertAll(Manifest,Directory.Path));
        Assert.Equal(Before,File.ReadAllBytes(Path.Combine(Directory.Path,AssetPath)));
    }
    [Fact]
    public void ConvertAll_ManifestWithoutGridMaterials_Fails(){
        using var Directory=new TemporaryDirectory();
        Assert.Throws<InvalidOperationException>(()=>WorldGridGlbConverter.ConvertAll(new WorldGridManifest(),Directory.Path));
    }
    private static WorldGridManifest CreateManifest(string AssetPath,string Material){
        var Manifest=new WorldGridManifest();
        Manifest.RenderVariants.Add(new WorldGridVariant { AssetPath=AssetPath,Materials=[Material] });
        Manifest.MaterialGrids[GridMaterial]=CreateParameters();
        return Manifest;
    }
    private static WorldGridParameters CreateParameters()=>new(){
        GridSizeCm=100f,
        SubGridDivisions=5,
        WireHalfWidth=5f/512f,
        Roughness=1f,
        SurfaceColor=[0.18f,0.18f,0.18f],
        GridColor=[0.026f,0.026f,0.026f],
        SubGridColor=[0.088f,0.088f,0.088f],
    };
    /// <summary>UEの書き出しと同じ形: 焼き付けテクスチャ(TEXCOORD_1)、2組のUV、TANGENT付きの1メッシュ。</summary>
    private static void WriteBakedFloor(string Path){
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var Png=GltfEmissiveTextureLuminanceTests.CreateRgbaPng(2,2,[10,20,30,255,40,50,60,255,70,80,90,255,100,110,120,255]);
        var Material=new MaterialBuilder("MI_PrototypeGrid_Gray_Floor").WithMetallicRoughness(0f,1f).WithChannelImage(KnownChannel.BaseColor,new MemoryImage(Png));
        var Mesh=new MeshBuilder<VertexPositionNormalTangent,VertexTexture2,VertexEmpty>("Floor");
        var Primitive=Mesh.UsePrimitive(Material);
        VertexBuilder<VertexPositionNormalTangent,VertexTexture2,VertexEmpty>Vertex(float X,float Z,float U,float V)=>new(
            new VertexPositionNormalTangent(new Vector3(X,0f,Z),Vector3.UnitY,new Vector4(1f,0f,0f,1f)),new VertexTexture2(new Vector2(U,V),new Vector2(V,U)));
        Primitive.AddQuadrangle(Vertex(-5f,-5f,0f,0f),Vertex(5f,-5f,1f,0f),Vertex(5f,5f,1f,1f),Vertex(-5f,5f,0f,1f));
        var Scene=new SceneBuilder();
        Scene.AddRigidMesh(Mesh,new NodeBuilder("Floor"));
        Scene.ToGltf2().SaveGLB(Path);
    }
    private sealed class TemporaryDirectory:IDisposable {
        public string Path { get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),$"world-grid-{Guid.NewGuid():N}");
        public TemporaryDirectory()=>System.IO.Directory.CreateDirectory(this.Path);
        public void Dispose()=>System.IO.Directory.Delete(this.Path,true);
    }
}
