using CSharpGameEngine.Rendering;
using System.Buffers.Binary;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class StageSceneDataBuilderTests {
    [WindowsOnlyFact]
    public void TexturedGltf_IsConvertedToCanonicalMaterialResourcesBeforeL0(){
        StageSceneDataBuilder Scene=StageSceneDataBuilder.Build(
            [(new Entity(0,1),new Transform(),new StaticMesh {
                AssetPath=Path.Combine("Fixtures","BoxTextured.glb"),
                Mobility=MeshMobility.Static,
            })],
            []);

        StageMaterialCreateInfo Material=Assert.Single(Scene.Materials);
        Assert.NotNull(Material.BaseColorTexture);
        Assert.NotNull(Material.NormalTexture);
        Assert.NotNull(Material.MetallicRoughnessTexture);
        Assert.NotNull(Material.EmissiveTexture);
        Assert.True(Material.BaseColorTexture.Width>1);
        Assert.True(Material.BaseColorTexture.Height>1);
        Assert.Equal(
            Material.BaseColorTexture.Width*Material.BaseColorTexture.Height*4,
            Material.BaseColorTexture.Pixels.Length);
    }

    /// <summary>M0098: a glTF material's <c>extras.CGE_world_grid</c> must reach the renderer's material.</summary>
    [WindowsOnlyFact]
    public void WorldGridGltf_ReachesTheStageMaterialAlongsideItsSurfaceColor(){
        var Directory=Path.Combine(Path.GetTempPath(),$"stage-world-grid-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Directory);
        try {
            var AssetPath=Path.Combine(Directory,"Floor.gltf");
            File.WriteAllText(AssetPath,CreateWorldGridGltf());
            StageSceneDataBuilder Scene=StageSceneDataBuilder.Build(
                [(new Entity(0,1),new Transform(),new StaticMesh { AssetPath=AssetPath,Mobility=MeshMobility.Static })],
                []);
            StageMaterialCreateInfo Material=Assert.Single(Scene.Materials);
            Assert.Equal(new Vector4(0.18f,0.18f,0.18f,1f),Material.BaseColorFactor);
            Assert.Equal(new StageWorldGrid(1f,5,0.009765625f,new Vector3(0.026f),new Vector3(0.088f)),Material.WorldGrid);
        } finally {
            System.IO.Directory.Delete(Directory,true);
        }
    }

    private static string CreateWorldGridGltf(){
        var Geometry=new byte[42];
        float[]Positions=[0f,0f,0f,1f,0f,0f,0f,1f,0f];
        for(var Index=0;Index<Positions.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(Index*4,4),Positions[Index]);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(36,2),0);
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(38,2),1);
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(40,2),2);
        return $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0 } ] } ],
              "materials": [ {
                "pbrMetallicRoughness": { "baseColorFactor": [0.18, 0.18, 0.18, 1.0], "metallicFactor": 0 },
                "extras": { "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 5, "wireHalfWidth": 0.009765625, "gridColor": [0.026, 0.026, 0.026], "subGridColor": [0.088, 0.088, 0.088] } }
              } ],
              "buffers": [ { "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(Geometry)}}", "byteLength": 42 } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;
    }

    [Fact]
    public void L0MaterialInputExposesNoEncodedTextureByteArrays(){
        var Properties=typeof(StageMaterialCreateInfo).GetProperties();

        Assert.DoesNotContain(Properties,Property=>Property.Name.EndsWith(
            "TextureBytes",StringComparison.Ordinal));
        Assert.DoesNotContain(Properties,Property=>Property.PropertyType==typeof(byte[]));
        Assert.DoesNotContain(
            typeof(RenderServer).GetMethods()
                .Where(Method=>Method.Name==nameof(RenderServer.Create))
                .SelectMany(Method=>Method.GetParameters()),
            Parameter=>Parameter.ParameterType==typeof(byte[]));
    }

    [Fact]
    public void EncodedTextureAdapterRejectsCorruptionInsteadOfFallingBack(){
        Assert.Null(RenderTextureArtifactAdapter.DecodeOptional([]));
        Assert.Throws<InvalidDataException>(()=>
            RenderTextureArtifactAdapter.DecodeOptional([1,2,3,4]));
    }
}
