using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace CSharpGameEngine.Import.Gltf.Tests;

public class GltfEmissiveImportTests {
    [Fact]
    public void ImportScene_EmissiveStrengthAndImportScale_ProducesNits(){
        var Material=new MaterialBuilder("Emitter")
            .WithMetallicRoughness()
            .WithEmissive(new Vector3(0.25f,0.5f,1f),strength:3f);
        var Mesh=new MeshBuilder<VertexPosition,VertexTexture1,VertexEmpty>("EmitterMesh");
        var Primitive=Mesh.UsePrimitive(Material);
        Primitive.AddTriangle(
            (new VertexPosition(0,0,0),new VertexTexture1(Vector2.Zero)),
            (new VertexPosition(1,0,0),new VertexTexture1(Vector2.UnitX)),
            (new VertexPosition(0,1,0),new VertexTexture1(Vector2.UnitY)));

        var SceneBuilder=new SceneBuilder();
        SceneBuilder.AddRigidMesh(Mesh,new NodeBuilder("EmitterNode"));
        var FixturePath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),$"gltf-emissive-{Guid.NewGuid():N}.glb");
        SceneBuilder.ToGltf2().SaveGLB(FixturePath);
        try {
            var Result=GltfImporter.ImportScene(FixturePath,new GltfImportOptions { EmissiveNitsPerUnit=200f });

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var MaterialResult=Assert.Single(Result.Scene!.Materials);
            Assert.Equal(new Vector3(0.25f,0.5f,1f),MaterialResult.EmissiveFactor);
            Assert.Equal(3f,MaterialResult.EmissiveStrength,precision:3);
            Assert.Equal(new Vector3(150f,300f,600f),MaterialResult.EmissiveNits);
            Assert.Equal(1f,MaterialResult.EmissiveTextureLuminanceScale);
        } finally {
            File.Delete(FixturePath);
        }
    }

    [Fact]
    public void ImportScene_NonPositiveEmissiveScale_ThrowsBeforeReadingFile(){
        var Options=new GltfImportOptions { EmissiveNitsPerUnit=0f };

        Assert.Throws<ArgumentOutOfRangeException>(()=>GltfImporter.ImportScene("unused.glb",Options));
    }
}
