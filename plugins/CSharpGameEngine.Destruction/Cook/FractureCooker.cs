using System.Text.Json;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Destruction.Cook;
/// <summary>調理側が読み取った、シーン内の <c>Destructible</c> 1つ。元形状と分割設定を保持する。</summary>
internal sealed record DestructibleSource(string Name,string Node,PhysicsShapeAsset Source,PrefractureCookSettings Settings,string SourceDescription);
/// <summary>
/// シーン内のすべての <c>CSharpGameEngine.Destruction.Destructible</c> を事前破砕する（ADR-0078/M0113、UE の Fracture モード）
/// 。出力先は <c>Assets/Destruction/&lt;FractureAsset&gt;.cgefracture</c>。元形状はノードの座標枠における <c>Center</c> と
/// <c>HalfExtents</c> で定義される <c>Collider</c> のボックス、または <c>SourceMesh</c> で指定された閉じた凸形状の <c>.glb</c>。分割には
/// エンジンのシード付きボロノイ調理処理（<see cref="PrefractureCooker"/>）を使い、同じシーンからは同じバイト列を生成する。失敗と警告は
/// <c>file: error DSTnnn: message</c> の各行で出力し、ビルドの Exec が診断情報に変換する。
/// </summary>
public sealed class FractureCooker:IAssetCooker {
    public string Name=>"Fracture";
    public IReadOnlyList<string> InputExtensions=>[".json"];
    public CookResult Cook(CookRequest Request){
        var SceneName=Path.GetFileName(Request.InputPath);
        if(!DestructibleScene.TryRead(Request.InputPath,Request.ProjectDirectory,out var Sources,out var Error)){ return CookResult.Failure(Error); }
        if(Sources.Count==0){
            return CookResult.Failure($"{SceneName}: error DST009: The scene has no {DestructionDefaults.DestructibleType}, so there is nothing to fracture.");
        }
        List<string>Outputs=[];
        List<string>Messages=[];
        foreach(var Group in Sources.GroupBy(static Source=>Source.Name,StringComparer.Ordinal).OrderBy(static Group=>Group.Key,StringComparer.Ordinal)){
            var First=Group.First();
            foreach(var Other in Group.Skip(1)){
                if(Other.Settings!=First.Settings||Other.Source.Fingerprint!=First.Source.Fingerprint){
                    return CookResult.Failure($"{SceneName}: error DST010: The destructibles '{First.Node}' and '{Other.Node}' share the FractureAsset '{Group.Key}' but not the source or the cut; give one of them another name.");
                }
            }
            if(!PrefractureCooker.TryGenerate(First.Source,First.Settings,out var Asset,out var FractureError)||Asset is null){
                return CookResult.Failure($"{SceneName}: error DST008: The destructible '{First.Node}' ({First.SourceDescription}) could not be fractured: {FractureError}");
            }
            var Relative=DestructionDefaults.AssetPath(Group.Key);
            var Destination=Path.Combine(Request.OutputDirectory,Relative.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.WriteAllBytes(Destination,PrefractureArtifact.ToBytes(Asset));
            Outputs.Add(Relative);
            Messages.Add($"{SceneName}: Fracture '{Group.Key}': {Asset.Fragments.Count} fragments, {Asset.ClusterCount} clusters, {Asset.ClusterLevelCount} cluster level(s), {Asset.Bonds.Count} bonds ({First.SourceDescription}).");
        }
        return new CookResult(true,Outputs,Messages);
    }
}
/// <summary>1つのシーン JSON から破壊可能オブジェクトを読み込む（<see cref="FractureCooker"/>）。</summary>
internal static class DestructibleScene {
    private const string TransformType="CSharpGameEngine.Entities.Transform";
    private const string ColliderType="CSharpGameEngine.Runtime.Physics.Collider";
    internal static bool TryRead(string ScenePath,string ProjectDirectory,out List<DestructibleSource>Sources,out string Error){
        Sources=[];
        Error=string.Empty;
        var SceneName=Path.GetFileName(ScenePath);
        JsonDocument Document;
        try {
            Document=JsonDocument.Parse(File.ReadAllText(ScenePath),new JsonDocumentOptions { AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip });
        }
        catch(Exception Exception)when(Exception is JsonException or IOException or UnauthorizedAccessException){
            Error=$"{SceneName}: error DST011: The scene could not be read: {Exception.Message}";
            return false;
        }
        using(Document){
            if(Document.RootElement.ValueKind!=JsonValueKind.Object||!Document.RootElement.TryGetProperty("root",out var Root)||Root.ValueKind!=JsonValueKind.Object){
                Error=$"{SceneName}: error DST011: The file has no scene root.";
                return false;
            }
            return TryReadNode(Root,SceneName,ProjectDirectory,Sources,out Error);
        }
    }
    private static bool TryReadNode(JsonElement Node,string SceneName,string ProjectDirectory,List<DestructibleSource>Sources,out string Error){
        Error=string.Empty;
        Dictionary<string,JsonElement>Components=new(StringComparer.Ordinal);
        if(Node.TryGetProperty("components",out var List)&&List.ValueKind==JsonValueKind.Array){
            foreach(var Component in List.EnumerateArray()){
                if(Component.ValueKind==JsonValueKind.Object&&Component.TryGetProperty("type",out var Type)&&Type.ValueKind==JsonValueKind.String){
                    Components[Type.GetString()!]=Component.TryGetProperty("members",out var Members)&&Members.ValueKind==JsonValueKind.Object?Members:default;
                }
            }
        }
        var Name=Node.TryGetProperty("name",out var NameElement)&&NameElement.ValueKind==JsonValueKind.String?NameElement.GetString()!:"(node)";
        if(Components.TryGetValue(DestructionDefaults.DestructibleType,out var Destructible)
            &&!TryReadDestructible(Destructible,Components,Name,SceneName,ProjectDirectory,Sources,out Error)){ return false; }
        if(Node.TryGetProperty("children",out var Children)&&Children.ValueKind==JsonValueKind.Array){
            foreach(var Child in Children.EnumerateArray()){
                if(Child.ValueKind==JsonValueKind.Object&&!TryReadNode(Child,SceneName,ProjectDirectory,Sources,out Error)){ return false; }
            }
        }
        return true;
    }
    private static bool TryReadDestructible(JsonElement Members,Dictionary<string,JsonElement>Components,string Node,string SceneName,string ProjectDirectory,List<DestructibleSource>Sources,out string Error){
        Error=string.Empty;
        string Problem(string Code,string Text)=>$"{SceneName}: error {Code}: The Destructible of '{Node}' {Text}";
        var FractureAsset=string.Empty;
        var FragmentCount=DestructionDefaults.FragmentCount;
        var Seed=DestructionDefaults.Seed;
        var ClusterCount=DestructionDefaults.ClusterCount;
        var ClusterLevels=DestructionDefaults.ClusterLevels;
        var SourceMesh=string.Empty;
        var Density=LogicalScalar.FromInt64(DestructionDefaults.Density);
        var Strength=LogicalScalar.FromInt64(DestructionDefaults.BondStrength);
        if(Members.ValueKind==JsonValueKind.Object){
            if(Members.TryGetProperty("FractureAsset",out var AssetElement)){
                if(AssetElement.ValueKind!=JsonValueKind.String){ Error=Problem("DST011","has an unreadable FractureAsset."); return false; }
                FractureAsset=AssetElement.GetString()!;
            }
            if(Members.TryGetProperty("SourceMesh",out var MeshElement)){
                if(MeshElement.ValueKind!=JsonValueKind.String){ Error=Problem("DST011","has an unreadable SourceMesh."); return false; }
                SourceMesh=MeshElement.GetString()!;
            }
            if(!TryReadInt(Members,"FragmentCount",ref FragmentCount)||!TryReadInt(Members,"Seed",ref Seed)||!TryReadInt(Members,"ClusterCount",ref ClusterCount)
                ||!TryReadInt(Members,"ClusterLevels",ref ClusterLevels)){
                Error=Problem("DST011","has a FragmentCount, Seed, ClusterCount or ClusterLevels that is not a whole number.");
                return false;
            }
            if(!TryReadScalar(Members,"Density",ref Density)||!TryReadScalar(Members,"BondStrength",ref Strength)){
                Error=Problem("DST011","has a Density or BondStrength that is not a finite number.");
                return false;
            }
        }
        if(!DestructionDefaults.IsValidName(FractureAsset)){
            Error=Problem("DST001",$"names no valid FractureAsset ('{FractureAsset}': 1 to 64 letters, digits, '-', '_' and '.').");
            return false;
        }
        if(FragmentCount is <2 or >256){
            Error=Problem("DST003",$"asks for {FragmentCount} fragments; a fracture has 2 to 256.");
            return false;
        }
        if(Density<=LogicalScalar.Zero||Strength<=LogicalScalar.Zero||ClusterCount<0){
            Error=Problem("DST003","needs a positive Density and BondStrength and a ClusterCount of 0 or more.");
            return false;
        }
        if(ClusterLevels is <1 or >PrefractureCookSettings.MaximumClusterLevels){
            Error=Problem("DST003",$"asks for {ClusterLevels} cluster levels; a fracture has 1 to {PrefractureCookSettings.MaximumClusterLevels}.");
            return false;
        }
        if(Components.TryGetValue(TransformType,out var Transform)&&Transform.ValueKind==JsonValueKind.Object&&Transform.TryGetProperty("Scale",out var ScaleElement)){
            if(!TryReadVector(ScaleElement,LogicalVector3.One,out var Scale)||Scale!=LogicalVector3.One){
                Error=Problem("DST004","has a scale; give the size with its Collider and keep the scale at one.");
                return false;
            }
        }
        PhysicsShapeAsset Source;
        string Description;
        if(SourceMesh.Length>0){
            if(!TryLoadMesh(FractureAsset,SourceMesh,ProjectDirectory,out Source,out var MeshError)){
                Error=Problem("DST007",MeshError);
                return false;
            }
            Description=$"mesh {SourceMesh}";
        }
        else {
            if(!Components.TryGetValue(ColliderType,out var Collider)){
                Error=Problem("DST006","has no Collider to cut and no SourceMesh.");
                return false;
            }
            var Shape=Collider.ValueKind==JsonValueKind.Object&&Collider.TryGetProperty("Shape",out var ShapeElement)&&ShapeElement.ValueKind==JsonValueKind.String?ShapeElement.GetString():"Box";
            if(Shape!="Box"){
                Error=Problem("DST006",$"has a {Shape} collider; the cook cuts a box collider or a SourceMesh.");
                return false;
            }
            var Center=LogicalVector3.Zero;
            var Half=new LogicalVector3(LogicalScalar.One/LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2));
            if(Collider.ValueKind==JsonValueKind.Object
                &&((Collider.TryGetProperty("Center",out var CenterElement)&&!TryReadVector(CenterElement,Center,out Center))
                    ||(Collider.TryGetProperty("HalfExtents",out var HalfElement)&&!TryReadVector(HalfElement,Half,out Half)))){
                Error=Problem("DST011","has a Collider with an unreadable Center or HalfExtents.");
                return false;
            }
            if(Half.R<=LogicalScalar.Zero||Half.U<=LogicalScalar.Zero||Half.F<=LogicalScalar.Zero){
                Error=Problem("DST006","has a Collider box without volume (every half extent must be positive).");
                return false;
            }
            Source=Box(FractureAsset,Center,Half);
            Description=$"box {Half.R.ToStringInvariant()} x {Half.U.ToStringInvariant()} x {Half.F.ToStringInvariant()} m half extents";
        }
        Sources.Add(new DestructibleSource(FractureAsset,Node,Source,new PrefractureCookSettings(FragmentCount,Seed,Density,Strength,ClusterCount,ClusterLevels),Description));
        return true;
    }
    /// <summary>ノードの座標枠内にある、外向きの巻き順を持つ12個の三角形で構成した閉じたボックス。</summary>
    internal static PhysicsShapeAsset Box(string Id,LogicalVector3 Center,LogicalVector3 Half){
        LogicalVector3[]Vertices=new LogicalVector3[8];
        for(var Index=0;Index<8;Index++){
            Vertices[Index]=Center+new LogicalVector3((Index&1)==0?-Half.R:Half.R,(Index&2)==0?-Half.U:Half.U,(Index&4)==0?-Half.F:Half.F);
        }
        // 各面の外向き方向：-R、+R、-U、+U、-F、+F。
        int[]Indices=[0,4,6,0,6,2, 1,3,7,1,7,5, 0,1,5,0,5,4, 2,6,7,2,7,3, 0,2,3,0,3,1, 4,5,7,4,7,6];
        return PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices);
    }
    private static bool TryLoadMesh(string Id,string AssetPath,string ProjectDirectory,out PhysicsShapeAsset Source,out string Error){
        Source=null!;
        Error=string.Empty;
        var FullPath=Path.GetFullPath(Path.Combine(ProjectDirectory,AssetPath.Replace('/',Path.DirectorySeparatorChar)));
        if(!File.Exists(FullPath)){
            Error=$"names the SourceMesh '{AssetPath}', which was not found.";
            return false;
        }
        var Result=GltfImporter.ImportScene(FullPath);
        if(!Result.Success||Result.Scene is not { }Imported){
            Error=$"names the SourceMesh '{AssetPath}', which could not be imported: {string.Join("; ",Result.Diagnostics)}";
            return false;
        }
        List<LogicalVector3>Vertices=[];
        List<int>Indices=[];
        foreach(var Item in Imported.Nodes){
            if(Item.MeshIndex is not { }MeshIndex){ continue; }
            var Data=Imported.Meshes[MeshIndex];
            var Offset=Vertices.Count;
            foreach(var Position in Data.Positions){
                var Placed=System.Numerics.Vector3.Transform(Position,Item.WorldTransform);
                if(!LogicalScalar.TryFromSingle(Placed.X,out var R)||!LogicalScalar.TryFromSingle(Placed.Y,out var U)||!LogicalScalar.TryFromSingle(Placed.Z,out var F)){
                    Error=$"names the SourceMesh '{AssetPath}', whose vertices are not finite numbers in range.";
                    return false;
                }
                Vertices.Add(new LogicalVector3(R,U,F));
            }
            foreach(var Index in Data.Indices){ Indices.Add(Offset+(int)Index); }
        }
        try {
            Source=PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices);
            return true;
        }
        catch(Exception Exception)when(Exception is ArgumentException or OverflowException){
            Error=$"names the SourceMesh '{AssetPath}', which is not a usable triangle mesh: {Exception.Message}";
            return false;
        }
    }
    private static bool TryReadInt(JsonElement Members,string Name,ref int Value){
        if(!Members.TryGetProperty(Name,out var Element)){ return true; }
        return Element.ValueKind==JsonValueKind.Number&&Element.TryGetInt32(out Value);
    }
    /// <summary>数値。JSON テキストをそのまま固定小数点に変換するため丸め誤差が生じない。「NaN」などの文字列は拒否する。</summary>
    private static bool TryReadScalar(JsonElement Members,string Name,ref LogicalScalar Value){
        if(!Members.TryGetProperty(Name,out var Element)){ return true; }
        return Element.ValueKind==JsonValueKind.Number&&LogicalScalar.TryParseInvariant(Element.GetRawText(),out Value);
    }
    private static bool TryReadVector(JsonElement Element,LogicalVector3 Default,out LogicalVector3 Value){
        Value=Default;
        if(Element.ValueKind!=JsonValueKind.Object){ return false; }
        var R=Default.R;
        var U=Default.U;
        var F=Default.F;
        if((Element.TryGetProperty("R",out var RElement)&&!TryReadNumber(RElement,out R))
            ||(Element.TryGetProperty("U",out var UElement)&&!TryReadNumber(UElement,out U))
            ||(Element.TryGetProperty("F",out var FElement)&&!TryReadNumber(FElement,out F))){ return false; }
        Value=new LogicalVector3(R,U,F);
        return true;
    }
    private static bool TryReadNumber(JsonElement Element,out LogicalScalar Value){
        Value=default;
        return Element.ValueKind==JsonValueKind.Number&&LogicalScalar.TryParseInvariant(Element.GetRawText(),out Value);
    }
}
