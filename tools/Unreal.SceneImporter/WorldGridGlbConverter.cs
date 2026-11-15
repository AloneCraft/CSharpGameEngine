using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpGameEngine.Import.Gltf;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// UnrealのM_PrototypeGrid系マテリアルを使う描画資産glbを、ワールド空間の手続き的グリッドへ書き換える(M0098)。
/// UEのグリッドはワールド座標へ投影した繰り返しテクスチャで、glTF書き出しの1024px焼き付けでは
/// 床全体をまとめた画像(しかもTEXCOORD_1)になり再現できない。焼き付けテクスチャを外し、
/// base colorをUEのSurfaceColorに戻し、グリッドの値をmaterial.extras.CGE_world_gridへ置く。
/// 書き換え済みのglbに再実行しても結果は同じ(冪等)。
/// </summary>
public static class WorldGridGlbConverter {
    public static int Run(string[]Arguments){
        if(Arguments.Length!=2){
            Console.Error.WriteLine("Usage: CSharpGameEngine.Unreal.SceneImporter worldgrid <manifest.json> <project-directory>");
            return 2;
        }
        var Manifest=JsonSerializer.Deserialize<WorldGridManifest>(File.ReadAllText(Path.GetFullPath(Arguments[0])),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ??throw new InvalidOperationException($"Could not deserialize '{Arguments[0]}'.");
        var Converted=ConvertAll(Manifest,Path.GetFullPath(Arguments[1]));
        Console.WriteLine($"World grid: {Converted} render variant(s) converted, {Manifest.MaterialGrids.Count} grid material(s) in the manifest.");
        return 0;
    }
    /// <summary>materialGridsのマテリアルを使う描画資産を書き換え、書き換えた(または既に変換済みだった)資産数を返す。</summary>
    public static int ConvertAll(WorldGridManifest Manifest,string ProjectDirectory){
        if(Manifest.MaterialGrids.Count==0){
            throw new InvalidOperationException("The manifest has no materialGrids; run tools/UnrealExport/add_material_grids_to_manifest.py or re-export.");
        }
        var Count=0;
        foreach(var Variant in Manifest.RenderVariants){
            var Grids=Variant.Materials.Where(Manifest.MaterialGrids.ContainsKey).ToArray();
            if(Grids.Length==0){
                continue;
            }
            if(Variant.Materials.Count!=1){
                throw new InvalidOperationException($"'{Variant.AssetPath}' mixes a grid material with others ({string.Join(", ",Variant.Materials)}); only single-material variants are supported.");
            }
            Convert(Path.Combine(ProjectDirectory,Variant.AssetPath),Manifest.MaterialGrids[Grids[0]]);
            Count++;
        }
        return Count;
    }
    /// <summary>
    /// 書き出されたglbの形(単一メッシュ・単一プリミティブ・単一マテリアル)を検証し、ジオメトリだけを
    /// 新しいモデルへ複製してマテリアルを作り直す。SharpGLTF.Coreにはテクスチャの取り外しと画像の削除が
    /// 無いため、元のモデルを編集せず複製する。複製するのはエンジンが読むPOSITION/NORMAL/TANGENT/
    /// TEXCOORD_0と添字で、グリッドは頂点UVを使わないので焼き付け用のTEXCOORD_1は落とす。
    /// </summary>
    private static void Convert(string GlbPath,WorldGridParameters Grid){
        ValidateSupported(GlbPath,Grid);
        var Source=ModelRoot.Load(GlbPath);
        if(Source.LogicalMaterials.Count!=1||Source.LogicalMeshes.Count!=1||Source.LogicalMeshes[0].Primitives.Count!=1||Source.LogicalSkins.Count!=0||Source.LogicalAnimations.Count!=0){
            throw new InvalidOperationException($"'{GlbPath}' must be one static mesh with one primitive and one material.");
        }
        var SourceMesh=Source.LogicalMeshes[0];
        var SourcePrimitive=SourceMesh.Primitives[0];
        var SourceNodes=Source.LogicalNodes.Where(Node=>Node.Mesh is not null).ToArray();
        if(SourceNodes.Length!=1){
            throw new InvalidOperationException($"'{GlbPath}' must have exactly one mesh node, found {SourceNodes.Length}.");
        }
        var Target=ModelRoot.CreateModel();
        var Material=Target.CreateMaterial(Source.LogicalMaterials[0].Name);
        Material.InitializePBRMetallicRoughness();
        var BaseColor=Material.FindChannel("BaseColor")!.Value;
        BaseColor.Color=new Vector4(Grid.SurfaceColor[0],Grid.SurfaceColor[1],Grid.SurfaceColor[2],1f);
        var MetallicRoughness=Material.FindChannel("MetallicRoughness")!.Value;
        MetallicRoughness.SetFactor("MetallicFactor",0f);
        MetallicRoughness.SetFactor("RoughnessFactor",Grid.Roughness);
        Material.Extras=new JsonObject {
            [ImportedWorldGrid.ExtrasKey]=new JsonObject {
                ["gridSizeMeters"]=Grid.GridSizeCm*0.01f,
                ["subGridDivisions"]=Grid.SubGridDivisions,
                ["wireHalfWidth"]=Grid.WireHalfWidth,
                ["gridColor"]=new JsonArray(Grid.GridColor[0],Grid.GridColor[1],Grid.GridColor[2]),
                ["subGridColor"]=new JsonArray(Grid.SubGridColor[0],Grid.SubGridColor[1],Grid.SubGridColor[2]),
            },
        };
        var Mesh=Target.CreateMesh(SourceMesh.Name);
        var Primitive=Mesh.CreatePrimitive();
        Primitive.SetVertexAccessor("POSITION",CreateVertexAccessor(Target,SourcePrimitive.GetVertexAccessor("POSITION").AsVector3Array(),AttributeFormat.Float3));
        Primitive.SetVertexAccessor("NORMAL",CreateVertexAccessor(Target,SourcePrimitive.GetVertexAccessor("NORMAL").AsVector3Array(),AttributeFormat.Float3));
        Primitive.SetVertexAccessor("TANGENT",CreateVertexAccessor(Target,SourcePrimitive.GetVertexAccessor("TANGENT").AsVector4Array(),AttributeFormat.Float4));
        Primitive.SetVertexAccessor("TEXCOORD_0",CreateVertexAccessor(Target,SourcePrimitive.GetVertexAccessor("TEXCOORD_0").AsVector2Array(),AttributeFormat.Float2));
        var Indices=SourcePrimitive.GetIndexAccessor().AsIndicesArray().ToArray();
        var IndexAccessor=Target.CreateAccessor();
        IndexAccessor.SetIndexData(Target.UseBufferView(MemoryMarshal.AsBytes(Indices.AsSpan()).ToArray(),0,null,0,BufferMode.ELEMENT_ARRAY_BUFFER),0,Indices.Length,IndexEncodingType.UNSIGNED_INT);
        Primitive.SetIndexAccessor(IndexAccessor);
        Primitive.DrawPrimitiveType=PrimitiveType.TRIANGLES;
        Primitive.Material=Material;
        var TargetNode=Target.UseScene("default").CreateNode(SourceNodes[0].Name);
        TargetNode.LocalTransform=SourceNodes[0].LocalTransform;
        TargetNode.Mesh=Mesh;
        var Temporary=GlbPath+".tmp";
        Target.SaveGLB(Temporary);
        File.Move(Temporary,GlbPath,true);
    }
    private static Accessor CreateVertexAccessor<T>(ModelRoot Target,IReadOnlyList<T>Values,AttributeFormat Format)where T:unmanaged{
        var Array=Values.ToArray();
        var View=Target.UseBufferView(MemoryMarshal.AsBytes(Array.AsSpan()).ToArray(),0,null,0,BufferMode.ARRAY_BUFFER);
        var Accessor=Target.CreateAccessor();
        Accessor.SetVertexData(View,0,Array.Length,Format);
        return Accessor;
    }
    private static void ValidateSupported(string GlbPath,WorldGridParameters Grid){
        if(Grid.GridSizeCm<=0f||Grid.SubGridDivisions<1||Grid.SurfaceColor.Length!=3||Grid.GridColor.Length!=3||Grid.SubGridColor.Length!=3){
            throw new InvalidOperationException($"The prototype grid parameters for '{GlbPath}' are incomplete.");
        }
    }
}
public sealed class WorldGridManifest {
    public List<WorldGridVariant>RenderVariants { get; set;}=[];
    public Dictionary<string,WorldGridParameters>MaterialGrids { get; set;}=[];
}
public sealed class WorldGridVariant {
    public string AssetPath { get; set;}=string.Empty;
    public List<string>Materials { get; set;}=[];
}
public sealed class WorldGridParameters {
    public float GridSizeCm { get; set;}
    public int SubGridDivisions { get; set;}
    public float WireHalfWidth { get; set;}
    public float Roughness { get; set;}=1f;
    public float[]SurfaceColor { get; set;}=[];
    public float[]GridColor { get; set;}=[];
    public float[]SubGridColor { get; set;}=[];
}
