using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.Import.Tests.Unreal;

/// <summary>
/// テンプレート用に UE からエクスポートした glb ファイルでは、マテリアルを保持しなければならない。レンダリングデバイスなし（<c>-nullrhi</c>）でエディターを実行すると、UE の glTF エクスポーターは空のマテリアルを書き出し、Vehicle と TopDown のテンプレートが灰色になっていたため、エクスポートスクリプトからこのオプションを外した。
/// </summary>
public sealed class TemplateExportMaterialTests {
    /// <summary>glb にメッシュはあるが、マテリアルに色、テクスチャ、extras、拡張のいずれの情報もない場合に true。</summary>
    internal static bool HasOnlyEmptyMaterials(string GltfJson){
        using var Document=JsonDocument.Parse(GltfJson);
        var Root=Document.RootElement;
        if(!Root.TryGetProperty("meshes",out var Meshes)||Meshes.GetArrayLength()==0){ return false; }
        if(!Root.TryGetProperty("materials",out var Materials)||Materials.GetArrayLength()==0){ return false; }
        foreach(var Material in Materials.EnumerateArray()){
            if(!IsEmpty(Material)){ return false; }
        }
        return true;
    }

    private static bool IsEmpty(JsonElement Material){
        foreach(var Property in Material.EnumerateObject()){
            if(Property.Name=="name"){ continue; }
            if(Property.Name=="pbrMetallicRoughness"&&Property.Value.ValueKind==JsonValueKind.Object&&!Property.Value.EnumerateObject().Any()){ continue; }
            return false;
        }
        return true;
    }

    internal static string JsonChunk(string Path){
        var Bytes=File.ReadAllBytes(Path);
        var Length=BitConverter.ToInt32(Bytes,12);
        return Encoding.UTF8.GetString(Bytes,20,Length);
    }

    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(System.IO.Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("CSharpGameEngine.slnx was not found above the test folder.");
    }

    [Fact]
    public void ACheckerFlagsMeshesWhoseMaterialsAreAllEmpty(){
        const string Empty="""{"meshes":[{}],"materials":[{"pbrMetallicRoughness":{}},{"name":"x","pbrMetallicRoughness":{}}]}""";
        const string Colored="""{"meshes":[{}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[1,0,0,1]}}]}""";
        const string Textured="""{"meshes":[{}],"materials":[{"pbrMetallicRoughness":{},"normalTexture":{"index":0}}]}""";
        const string GridOnly="""{"meshes":[{}],"materials":[{"pbrMetallicRoughness":{},"extras":{"CGE_world_grid":{}}}]}""";
        const string Animation="""{"animations":[{}]}""";

        Assert.True(HasOnlyEmptyMaterials(Empty));
        Assert.False(HasOnlyEmptyMaterials(Colored));
        Assert.False(HasOnlyEmptyMaterials(Textured));
        Assert.False(HasOnlyEmptyMaterials(GridOnly));
        Assert.False(HasOnlyEmptyMaterials(Animation));
    }

    [Fact]
    public void EveryTemplateGlbWithMeshesKeepsItsMaterials(){
        var Root=RepositoryRoot();
        var Files=Directory.EnumerateDirectories(System.IO.Path.Combine(Root,"Templates"),"CSharpGameEngine.Templates.*")
            .SelectMany(static Template=>Directory.EnumerateFiles(System.IO.Path.Combine(Template,"Assets"),"*.glb",SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(Files);

        var Greyed=Files.Where(File=>HasOnlyEmptyMaterials(JsonChunk(File))).Select(File=>System.IO.Path.GetRelativePath(Root,File)).ToArray();

        Assert.True(Greyed.Length==0,"These exported glb files have only empty materials (export with a render device, not -nullrhi): "+string.Join(", ",Greyed));
    }
}
