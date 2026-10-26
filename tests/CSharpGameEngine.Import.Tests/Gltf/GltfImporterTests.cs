using System.Numerics;

namespace CSharpGameEngine.Import.Gltf.Tests;

public class GltfImporterTests {
    private static string FixturePath(string FileName)=>Path.Combine(AppContext.BaseDirectory,"Fixtures",FileName);

    [Fact]
    public void Import_BoxGlb_Succeeds_WithPositionsNormalsAndIndices(){
        GltfImportResult Result=GltfImporter.Import(FixturePath("Box.glb"));

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotNull(Result.Mesh);
        Assert.Equal(Result.Mesh.Positions.Count,Result.Mesh.Normals.Count);
        Assert.True(Result.Mesh.Indices.Count>0);
        Assert.Equal(0,Result.Mesh.Indices.Count%3);
        // Box.glb に TEXCOORD_0 はないため UV 合成の Warning は想定どおり。どちらの場合も Errors はない。
        Assert.DoesNotContain(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Error);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Warning&&D.Message.Contains("TEXCOORD_0"));

        foreach(var Index in Result.Mesh.Indices){
            Assert.True(Index<Result.Mesh.Positions.Count);
        }
    }

    [Fact]
    public void Import_BoxGlb_HasSynthesizedUvsNoTextureAndRedFactor(){
        GltfImportResult Result=GltfImporter.Import(FixturePath("Box.glb"));

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        var Mesh=Result.Mesh!;

        Assert.Equal(Mesh.Positions.Count,Mesh.Uvs.Count);
        Assert.All(Mesh.Uvs,Uv=>Assert.Equal(Vector2.Zero,Uv));
        Assert.Null(Mesh.BaseColorTextureBytes);

        // 実際の Box.glb の JSON を解析して確認済み：pbrMetallicRoughness.baseColorFactor == [0.8, 0, 0, 1]。
        Assert.Equal(0.8f,Mesh.BaseColorFactor.X,precision:2);
        Assert.Equal(0f,Mesh.BaseColorFactor.Y,precision:2);
        Assert.Equal(0f,Mesh.BaseColorFactor.Z,precision:2);
        Assert.Equal(1f,Mesh.BaseColorFactor.W,precision:2);
    }

    [Fact]
    public void Import_BoxGlb_BoundingBox_IsPlausible(){
        GltfImportResult Result=GltfImporter.Import(FixturePath("Box.glb"));

        Assert.True(Result.Success);
        BoundingBox Bounds=Result.Mesh!.Bounds;

        Assert.True(Bounds.Min.X<Bounds.Max.X);
        Assert.True(Bounds.Min.Y<Bounds.Max.Y);
        Assert.True(Bounds.Min.Z<Bounds.Max.Z);
        Assert.True(Bounds.BoundingSphereRadius>0f);
        // Khronos の「Box」サンプルは小さな単位相当の立方体である。壊れやすい厳密なリテラル値ではなく、
        // 妥当な大きさの小物体であることを検証する。
        Assert.InRange(Bounds.BoundingSphereRadius,0.01f,100f);
    }

    [Fact]
    public void Import_TriangleGltf_MissingNormal_SynthesizesFlatNormalsWithWarning(){
        GltfImportResult Result=GltfImporter.Import(FixturePath("Triangle.gltf"));

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotNull(Result.Mesh);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Warning&&D.Message.Contains("NORMAL"));
        Assert.Contains(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Warning&&D.Message.Contains("TEXCOORD_0"));

        // 法線がない場合は三角形全体を展開する：三角形 1 つが、共有されない頂点 3 つになる。
        Assert.Equal(3,Result.Mesh.Positions.Count);
        Assert.Equal(3,Result.Mesh.Normals.Count);
        Assert.Equal(3,Result.Mesh.Uvs.Count);
        Assert.Equal(3,Result.Mesh.Indices.Count);
        Assert.All(Result.Mesh.Uvs,Uv=>Assert.Equal(Vector2.Zero,Uv));
        Assert.Null(Result.Mesh.BaseColorTextureBytes);
        Assert.Equal(new Vector4(1,1,1,1),Result.Mesh.BaseColorFactor);

        foreach(System.Numerics.Vector3 Normal in Result.Mesh.Normals){
            Assert.Equal(1.0f,Normal.Length(),precision:3);
        }

        // 三角形 1 つの平坦な面法線は、合成された 3 頂点すべてで共有される。
        Assert.Equal(Result.Mesh.Normals[0],Result.Mesh.Normals[1]);
        Assert.Equal(Result.Mesh.Normals[0],Result.Mesh.Normals[2]);
    }

    [Fact]
    public void Import_BoxTexturedGlb_Succeeds_WithTextureBytesUvsAndDefaultFactor(){
        GltfImportResult Result=GltfImporter.Import(FixturePath("BoxTextured.glb"));

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        var Mesh=Result.Mesh!;

        // このフィクスチャには NORMAL と TEXCOORD_0 の両方があるため、展開は行われない。
        Assert.Equal(Mesh.Positions.Count,Mesh.Uvs.Count);
        Assert.DoesNotContain(Result.Diagnostics,D=>D.Severity is GltfImportDiagnosticSeverity.Error or GltfImportDiagnosticSeverity.Warning);

        Assert.NotNull(Mesh.BaseColorTextureBytes);
        Assert.True(Mesh.BaseColorTextureBytes!.Length>0);
        // PNGのマジックナンバー：0x89 'P' 'N' 'G'。
        Assert.Equal(0x89,Mesh.BaseColorTextureBytes[0]);
        Assert.Equal((byte)'P',Mesh.BaseColorTextureBytes[1]);
        Assert.Equal((byte)'N',Mesh.BaseColorTextureBytes[2]);
        Assert.Equal((byte)'G',Mesh.BaseColorTextureBytes[3]);

        // 実際の BoxTextured.glb の JSON を解析して確認済み：baseColorFactor の明示指定なし -> glTF の既定値 (1,1,1,1)。
        Assert.Equal(new Vector4(1,1,1,1),Mesh.BaseColorFactor);
    }

    [Fact]
    public void Import_FileDoesNotExist_ReturnsFailedWithError(){
        var MissingPath=FixturePath("DoesNotExist.glb");

        GltfImportResult Result=GltfImporter.Import(MissingPath);

        Assert.False(Result.Success);
        Assert.Null(Result.Mesh);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Error&&D.Message.Contains(MissingPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_NullOrWhitespacePath_Throws(string?Path){
        // ArgumentException.ThrowIfNullOrWhiteSpace は、null に対しては特に ArgumentNullException をスローする。
        // null の場合と、空文字列または空白のみの場合の通常の ArgumentException は、どちらもここでは許容される。
        Assert.ThrowsAny<ArgumentException>(()=>GltfImporter.Import(Path!));
    }

    [Fact]
    public void Import_MalformedFile_ReturnsFailedNotException(){
        GltfImportResult Result=GltfImporter.Import(FixturePath("Malformed.glb"));

        Assert.False(Result.Success);
        Assert.Null(Result.Mesh);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Error);
    }
}
