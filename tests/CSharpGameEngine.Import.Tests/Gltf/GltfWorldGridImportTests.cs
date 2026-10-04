using System.Buffers.Binary;
using System.Numerics;
namespace CSharpGameEngine.Import.Gltf.Tests;
/// <summary>
/// The engine has one UV set per vertex. A texture bound to another UV set must be reported and
/// left out instead of being sampled through TEXCOORD_0 (Unreal's baked prototype grid floor used
/// TEXCOORD_1 and rendered as scrambled triangles). The procedural world grid (M0098) travels as
/// <c>extras.CGE_world_grid</c> on the material.
/// </summary>
public class GltfWorldGridImportTests {
    [Fact]
    public void ImportScene_TextureOnASecondUvSet_IsReportedAndNotSampledThroughTheFirst(){
        var Result=ImportFixture(BaseColorTexCoord:1,Extras:null);
        Assert.Contains(Result.Diagnostics,Item=>Item.Severity==GltfImportDiagnosticSeverity.Warning&&Item.Message.Contains("TEXCOORD_1",StringComparison.Ordinal));
        Assert.Null(Assert.Single(Result.Scene!.Materials).BaseColorTextureBytes);
    }
    [Fact]
    public void ImportScene_TextureOnTheFirstUvSet_IsKeptWithoutWarnings(){
        var Result=ImportFixture(BaseColorTexCoord:0,Extras:null);
        Assert.DoesNotContain(Result.Diagnostics,Item=>Item.Message.Contains("TEXCOORD_",StringComparison.Ordinal));
        Assert.NotNull(Assert.Single(Result.Scene!.Materials).BaseColorTextureBytes);
    }
    [Fact]
    public void ImportScene_WorldGridExtras_ReachTheMaterialAlongsideItsSurfaceColor(){
        var Result=ImportFixture(BaseColorTexCoord:null,Extras:"""{ "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 5, "wireHalfWidth": 0.009765625, "gridColor": [0.026, 0.026, 0.026], "subGridColor": [0.088, 0.088, 0.088] } }""");
        var Material=Assert.Single(Result.Scene!.Materials);
        var Grid=Assert.IsType<ImportedWorldGrid>(Material.WorldGrid);
        Assert.Equal(1f,Grid.GridSizeMeters);
        Assert.Equal(5,Grid.SubGridDivisions);
        Assert.Equal(0.009765625f,Grid.WireHalfWidth);
        Assert.Equal(new Vector3(0.026f),Grid.GridColor);
        Assert.Equal(new Vector3(0.088f),Grid.SubGridColor);
        Assert.Equal(new Vector4(0.18f,0.18f,0.18f,1f),Material.BaseColorFactor);
        Assert.DoesNotContain(Result.Diagnostics,Item=>Item.Message.Contains("CGE_world_grid",StringComparison.Ordinal));
    }
    [Fact]
    public void ImportScene_MaterialWithoutWorldGridExtras_HasNoWorldGrid(){
        var Result=ImportFixture(BaseColorTexCoord:null,Extras:null);
        Assert.Null(Assert.Single(Result.Scene!.Materials).WorldGrid);
    }
    [Theory]
    [InlineData("""{ "CGE_world_grid": { "gridSizeMeters": 0.0, "subGridDivisions": 5, "wireHalfWidth": 0.01, "gridColor": [0,0,0], "subGridColor": [0,0,0] } }""")]
    [InlineData("""{ "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 0, "wireHalfWidth": 0.01, "gridColor": [0,0,0], "subGridColor": [0,0,0] } }""")]
    [InlineData("""{ "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 5, "wireHalfWidth": 0.6, "gridColor": [0,0,0], "subGridColor": [0,0,0] } }""")]
    [InlineData("""{ "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 5, "wireHalfWidth": 0.01, "gridColor": [0,0], "subGridColor": [0,0,0] } }""")]
    public void ImportScene_InvalidWorldGridExtras_AreReportedInsteadOfBeingDropped(string Extras){
        var Result=ImportFixture(BaseColorTexCoord:null,Extras);
        Assert.Contains(Result.Diagnostics,Item=>Item.Severity==GltfImportDiagnosticSeverity.Warning&&Item.Message.Contains("CGE_world_grid",StringComparison.Ordinal));
        Assert.Null(Assert.Single(Result.Scene!.Materials).WorldGrid);
    }
    private static GltfSceneImportResult ImportFixture(int?BaseColorTexCoord,string?Extras){
        var FixturePath=Path.Combine(Path.GetTempPath(),$"gltf-world-grid-{Guid.NewGuid():N}.gltf");
        File.WriteAllText(FixturePath,CreateScene(BaseColorTexCoord,Extras));
        try {
            var Result=GltfImporter.ImportScene(FixturePath);
            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            return Result;
        } finally {
            File.Delete(FixturePath);
        }
    }
    private static string CreateScene(int?BaseColorTexCoord,string?Extras){
        var Png=Convert.ToBase64String(GltfEmissiveTextureLuminanceTests.CreateRgbaPng(1,1,[128,128,128,255]));
        var Geometry=new byte[100];
        float[]Positions=[0f,0f,0f,1f,0f,0f,0f,1f,0f];
        for(var Index=0;Index<Positions.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(Index*4,4),Positions[Index]);
        }
        float[]Uvs=[0f,0f,1f,0f,0f,1f];
        for(var Index=0;Index<Uvs.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(36+Index*4,4),Uvs[Index]);
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(60+Index*4,4),Uvs[Index]);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(84,2),0);
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(86,2),1);
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(88,2),2);
        var BaseColorTexture=BaseColorTexCoord is null
            ?string.Empty
            :$$""","baseColorTexture": { "index": 0, "texCoord": {{BaseColorTexCoord}} }""";
        var MaterialExtras=Extras is null?string.Empty:$$""","extras": {{Extras}}""";
        return $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ {
                "attributes": { "POSITION": 0, "TEXCOORD_0": 1, "TEXCOORD_1": 2 },
                "indices": 3,
                "material": 0
              } ] } ],
              "materials": [ {
                "pbrMetallicRoughness": { "baseColorFactor": [0.18, 0.18, 0.18, 1.0]{{BaseColorTexture}} }{{MaterialExtras}}
              } ],
              "textures": [ { "source": 0 } ],
              "images": [ { "uri": "data:image/png;base64,{{Png}}" } ],
              "buffers": [ { "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(Geometry)}}", "byteLength": 100 } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 24 },
                { "buffer": 0, "byteOffset": 60, "byteLength": 24 },
                { "buffer": 0, "byteOffset": 84, "byteLength": 6 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC2" },
                { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" },
                { "bufferView": 3, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;
    }
}
