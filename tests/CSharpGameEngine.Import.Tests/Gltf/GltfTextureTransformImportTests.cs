using System.Buffers.Binary;
using System.Numerics;
namespace CSharpGameEngine.Import.Gltf.Tests;
/// <summary>
/// KHR_texture_transform (used by Unreal's GLTFExporter for baked material atlases) must reach the
/// vertex UVs; ignoring it samples the wrong atlas region, e.g. the dark gutters between islands.
/// </summary>
public class GltfTextureTransformImportTests {
    private static readonly Vector2[]SourceUvs=[new(0f,0f),new(1f,0f),new(0f,1f)];
    [Fact]
    public void ImportScene_BaseColorTextureTransform_IsAppliedToTheVertexUvs(){
        var Mesh=ImportSingleMesh(BaseColorTransform:"\"offset\": [0.25, -0.5], \"scale\": [2, 0.5]",NormalTransform:null,out var Diagnostics);
        AssertUvs([new(0.25f,-0.5f),new(2.25f,-0.5f),new(0.25f,0f)],Mesh);
        Assert.DoesNotContain(Diagnostics,Item=>Item.Message.Contains("KHR_texture_transform",StringComparison.Ordinal));
    }
    [Fact]
    public void ImportScene_TextureTransformRotation_FollowsTheSpecificationMatrix(){
        // uv' = T * R * S * uv with R = [cos sin; -sin cos]: a quarter turn maps +u to -v and +v to +u.
        var Mesh=ImportSingleMesh(BaseColorTransform:"\"rotation\": 1.5707963",NormalTransform:null,out _);
        AssertUvs([new(0f,0f),new(0f,-1f),new(1f,0f)],Mesh);
    }
    [Fact]
    public void ImportScene_ChannelTransformsThatDisagree_AreReported(){
        ImportSingleMesh(BaseColorTransform:"\"scale\": [2, 2]",NormalTransform:"\"scale\": [3, 3]",out var Diagnostics);
        Assert.Contains(Diagnostics,Item=>Item.Severity==GltfImportDiagnosticSeverity.Warning&&Item.Message.Contains("KHR_texture_transform",StringComparison.Ordinal));
    }
    private static void AssertUvs(Vector2[]Expected,ImportedMeshData Mesh){
        Assert.Equal(Expected.Length,Mesh.Uvs.Count);
        for(var Index=0;Index<Expected.Length;Index++){
            Assert.True(Vector2.Distance(Expected[Index],Mesh.Uvs[Index])<1e-5f,$"UV {Index}: expected {Expected[Index]}, actual {Mesh.Uvs[Index]}");
        }
    }
    private static ImportedMeshData ImportSingleMesh(string BaseColorTransform,string?NormalTransform,out IReadOnlyList<GltfImportDiagnostic>Diagnostics){
        var FixturePath=Path.Combine(Path.GetTempPath(),$"gltf-texture-transform-{Guid.NewGuid():N}.gltf");
        File.WriteAllText(FixturePath,CreateScene(BaseColorTransform,NormalTransform));
        try {
            var Result=GltfImporter.ImportScene(FixturePath);
            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            Diagnostics=Result.Diagnostics;
            return Assert.Single(Result.Scene!.Meshes);
        } finally {
            File.Delete(FixturePath);
        }
    }
    private static string CreateScene(string BaseColorTransform,string?NormalTransform){
        var Png=Convert.ToBase64String(GltfEmissiveTextureLuminanceTests.CreateRgbaPng(1,1,[128,128,128,255]));
        var Geometry=new byte[66];
        float[]Positions=[0f,0f,0f,1f,0f,0f,0f,1f,0f];
        for(var Index=0;Index<Positions.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(Index*4,4),Positions[Index]);
        }
        for(var Index=0;Index<SourceUvs.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(36+Index*8,4),SourceUvs[Index].X);
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(40+Index*8,4),SourceUvs[Index].Y);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(60,2),0);
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(62,2),1);
        BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(64,2),2);
        var NormalTexture=NormalTransform is null
            ?string.Empty
            :$$""","normalTexture": { "index": 0, "extensions": { "KHR_texture_transform": { {{NormalTransform}} } } }""";
        return $$"""
            {
              "asset": { "version": "2.0" },
              "extensionsUsed": [ "KHR_texture_transform" ],
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ {
                "attributes": { "POSITION": 0, "TEXCOORD_0": 1 },
                "indices": 2,
                "material": 0
              } ] } ],
              "materials": [ {
                "pbrMetallicRoughness": {
                  "baseColorTexture": { "index": 0, "extensions": { "KHR_texture_transform": { {{BaseColorTransform}} } } }
                }{{NormalTexture}}
              } ],
              "textures": [ { "source": 0 } ],
              "images": [ { "uri": "data:image/png;base64,{{Png}}" } ],
              "buffers": [ { "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(Geometry)}}", "byteLength": 66 } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 24 },
                { "buffer": 0, "byteOffset": 60, "byteLength": 6 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
                { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC2" },
                { "bufferView": 2, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;
    }
}
