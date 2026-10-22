using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation.Cook;
/// <summary>ナビメッシュを作る領域(<c>NavMeshBounds</c>のノード)。同じ<see cref="Id"/>の領域は、1つのナビメッシュに合わせる。</summary>
internal sealed class NavVolume(string Id,string Scene,LogicalVector3 Min,LogicalVector3 Max,NavMeshSettings Settings) {
    public string Id { get; }=Id;
    public string Scene { get; }=Scene;
    public LogicalVector3 Min { get; }=Min;
    public LogicalVector3 Max { get; }=Max;
    public NavMeshSettings Settings { get; }=Settings;
}
/// <summary>世界の座標の三角形。</summary>
internal readonly record struct NavTriangle(LogicalVector3 A,LogicalVector3 B,LogicalVector3 C);
/// <summary>
/// シーンの JSON から、ナビメッシュに要る2つを取り出す: 領域(<c>CSharpGameEngine.Navigation.NavMeshBounds</c>)と、静的な衝突形状。
/// 衝突形状は物理と同じ元から取る: <c>StaticMesh</c>と<c>StaticMeshCollider</c>を持つノードの三角形(glTF のノードの変換も含め、世界の変換で置く)と、
/// <c>RigidBody</c>を持たない<c>Collider</c>の箱。<c>RigidBody</c>を持つノード(動く物)とトリガーは含めない。球・カプセル・凸形状の<c>Collider</c>は読まずに、メッセージにする。
/// </summary>
internal sealed class SceneGeometry {
    private const string TransformType="CSharpGameEngine.Entities.Transform";
    private const string StaticMeshType="CSharpGameEngine.Runtime.Windows.StaticMesh";
    private const string StaticMeshColliderType="CSharpGameEngine.Runtime.Physics.StaticMeshCollider";
    private const string ColliderType="CSharpGameEngine.Runtime.Physics.Collider";
    private const string RigidBodyType="CSharpGameEngine.Runtime.Physics.RigidBody";
    internal const string BoundsType="CSharpGameEngine.Navigation.NavMeshBounds";
    private readonly string ProjectDirectory;
    private readonly Dictionary<string,(Vector3[]Positions,int[]Indices)>Meshes=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string>MissingMeshes=new(StringComparer.OrdinalIgnoreCase);
    private SceneGeometry(string ProjectDirectory){
        this.ProjectDirectory=ProjectDirectory;
    }
    public List<NavTriangle>Triangles { get; }=[];
    public List<NavVolume>Volumes { get; }=[];
    public List<string>Messages { get; }=[];
    /// <summary>
    /// <paramref name="InputScene"/>の領域を、<paramref name="ProjectDirectory"/>の<c>Scenes</c>にある起動シーン(<c>startupOrder</c>を持つもの)と<paramref name="InputScene"/>の
    /// 静的な衝突形状に対して作る。UE のレベルが、読み込まれている全レベルの形状でナビメッシュを作るのと同じ。起動時に読まれないシーン(実行中に出すもの)は含めない。
    /// </summary>
    internal static bool TryLoad(string InputScene,string ProjectDirectory,out SceneGeometry Geometry,out string Error){
        Geometry=new SceneGeometry(ProjectDirectory);
        Error=string.Empty;
        var Input=Path.GetFullPath(InputScene);
        List<string>Scenes=[Input];
        var ScenesDirectory=Path.Combine(ProjectDirectory,"Scenes");
        if(Directory.Exists(ScenesDirectory)){
            Scenes.AddRange(Directory.EnumerateFiles(ScenesDirectory,"*.json",SearchOption.AllDirectories).Select(Path.GetFullPath)
                .Where(Path=>!string.Equals(Path,Input,StringComparison.OrdinalIgnoreCase)).OrderBy(static Path=>Path,StringComparer.Ordinal));
        }
        foreach(var Scene in Scenes){
            JsonDocument Document;
            try {
                Document=JsonDocument.Parse(File.ReadAllText(Scene),new JsonDocumentOptions { AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip });
            }
            catch(Exception Exception)when(Exception is JsonException or IOException or UnauthorizedAccessException){
                if(string.Equals(Scene,Input,StringComparison.OrdinalIgnoreCase)){
                    Error=$"{Path.GetFileName(Scene)}: error NAV001: The scene could not be read: {Exception.Message}";
                    return false;
                }
                Geometry.Messages.Add($"{Path.GetFileName(Scene)}: warning NAV002: The scene could not be read and adds no collision: {Exception.Message}");
                continue;
            }
            using(Document){
                var Root=Document.RootElement;
                var Own=string.Equals(Scene,Input,StringComparison.OrdinalIgnoreCase);
                if(Root.ValueKind!=JsonValueKind.Object||!Root.TryGetProperty("root",out var RootNode)||RootNode.ValueKind!=JsonValueKind.Object){
                    if(Own){
                        Error=$"{Path.GetFileName(Scene)}: error NAV001: The file has no scene root.";
                        return false;
                    }
                    continue;
                }
                if(!Own&&!Root.TryGetProperty("startupOrder",out _)){ continue; }
                var Version=Root.TryGetProperty("schemaVersion",out var VersionElement)&&VersionElement.ValueKind==JsonValueKind.Number&&VersionElement.TryGetInt32(out var Parsed)?Parsed:3;
                var KeepTriangles=Geometry.Triangles.Count;
                if(!Geometry.TryReadNode(RootNode,LogicalAffineTransform.Identity,Version,Path.GetFileName(Scene),Own,out Error)){
                    if(Own){ return false; }
                    // 別のシーンの不備はそのシーンのビルドの診断が報告する。ここでは、そのシーンの形状を足さずに警告だけ出す。
                    Geometry.Triangles.RemoveRange(KeepTriangles,Geometry.Triangles.Count-KeepTriangles);
                    Geometry.Messages.Add(Error.Replace(": error ",": warning ",StringComparison.Ordinal));
                    Error=string.Empty;
                }
            }
        }
        if(Geometry.MissingMeshes.Count>0){
            Error=$"{Path.GetFileName(InputScene)}: error NAV003: The mesh files a collider needs were not found: {string.Join(", ",Geometry.MissingMeshes.OrderBy(static Name=>Name,StringComparer.Ordinal))}.";
            return false;
        }
        return true;
    }
    private bool TryReadNode(JsonElement Node,LogicalAffineTransform Parent,int Version,string Scene,bool Own,out string Error){
        Error=string.Empty;
        Dictionary<string,JsonElement>Components=new(StringComparer.Ordinal);
        if(Node.TryGetProperty("components",out var List)&&List.ValueKind==JsonValueKind.Array){
            foreach(var Component in List.EnumerateArray()){
                if(Component.ValueKind==JsonValueKind.Object&&Component.TryGetProperty("type",out var Type)&&Type.ValueKind==JsonValueKind.String){
                    Components[Type.GetString()!]=Component.TryGetProperty("members",out var Members)&&Members.ValueKind==JsonValueKind.Object?Members:default;
                }
            }
        }
        var Name=Node.TryGetProperty("name",out var NameElement)&&NameElement.ValueKind==JsonValueKind.String?NameElement.GetString():"(node)";
        var World=Parent;
        if(Components.TryGetValue(TransformType,out var Transform)){
            if(!TryReadTransform(Transform,Version,out var Local)||!LogicalAffineTransform.TryMultiply(Local,Parent,out World)){
                Error=$"{Scene}: error NAV004: The Transform of '{Name}' is not a valid placement.";
                return false;
            }
        }
        if(Own&&Components.TryGetValue(BoundsType,out var Bounds)){
            if(!this.TryReadVolume(Bounds,Version,World,Scene,Name!,out Error)){ return false; }
        }
        var Dynamic=Components.TryGetValue(RigidBodyType,out _);
        if(!Dynamic){
            if(Components.TryGetValue(StaticMeshType,out var Mesh)&&Components.ContainsKey(StaticMeshColliderType)
                &&Mesh.ValueKind==JsonValueKind.Object&&Mesh.TryGetProperty("AssetPath",out var AssetElement)&&AssetElement.ValueKind==JsonValueKind.String){
                if(!this.TryAddMesh(AssetElement.GetString()!,World,Scene,Name!,out Error)){ return false; }
            }
            if(Components.TryGetValue(ColliderType,out var Collider)){
                if(!this.TryAddCollider(Collider,Version,World,Scene,Name!,out Error)){ return false; }
            }
        }
        if(Node.TryGetProperty("children",out var Children)&&Children.ValueKind==JsonValueKind.Array){
            foreach(var Child in Children.EnumerateArray()){
                if(Child.ValueKind==JsonValueKind.Object&&!this.TryReadNode(Child,World,Version,Scene,Own,out Error)){ return false; }
            }
        }
        return true;
    }
    private static bool TryReadTransform(JsonElement Members,int Version,out LogicalAffineTransform Local){
        Local=LogicalAffineTransform.Identity;
        if(Members.ValueKind!=JsonValueKind.Object){ return true; }
        var Position=LogicalVector3.Zero;
        var Rotation=LogicalQuaternion.Identity;
        var Scale=LogicalVector3.One;
        if(Members.TryGetProperty("Position",out var PositionElement)&&!TryReadVector(PositionElement,LogicalVector3.Zero,out Position)){ return false; }
        if(Members.TryGetProperty("Scale",out var ScaleElement)&&!TryReadVector(ScaleElement,LogicalVector3.One,out Scale)){ return false; }
        if(Members.TryGetProperty("Rotation",out var RotationElement)&&!TryReadRotation(RotationElement,Version,out Rotation)){ return false; }
        return LogicalAffineTransform.TryCreateTrs(Position,Rotation,Scale,out Local);
    }
    /// <summary>数。JSON の数の文字をそのまま固定小数点にして、丸めの誤差を持ち込まない。</summary>
    internal static bool TryReadScalar(JsonElement Element,out LogicalScalar Value){
        Value=default;
        if(Element.ValueKind!=JsonValueKind.Number){ return false; }
        if(LogicalScalar.TryParseInvariant(Element.GetRawText(),out Value)){ return true; }
        return Element.TryGetSingle(out var Single)&&LogicalScalar.TryFromSingle(Single,out Value);
    }
    internal static bool TryReadVector(JsonElement Element,LogicalVector3 Default,out LogicalVector3 Value){
        Value=Default;
        if(Element.ValueKind!=JsonValueKind.Object){ return false; }
        var R=Default.R;
        var U=Default.U;
        var F=Default.F;
        if((Element.TryGetProperty("R",out var RElement)&&!TryReadScalar(RElement,out R))
            ||(Element.TryGetProperty("U",out var UElement)&&!TryReadScalar(UElement,out U))
            ||(Element.TryGetProperty("F",out var FElement)&&!TryReadScalar(FElement,out F))){ return false; }
        Value=new LogicalVector3(R,U,F);
        return true;
    }
    /// <summary>回転。<c>EulerRuf</c>は版 2 以降で回転数(1 = 1周)、版 1 でラジアン。</summary>
    private static bool TryReadRotation(JsonElement Element,int Version,out LogicalQuaternion Rotation){
        Rotation=LogicalQuaternion.Identity;
        if(Element.ValueKind!=JsonValueKind.Object){ return false; }
        if(Element.TryGetProperty("EulerRuf",out var EulerElement)){
            if(!TryReadVector(EulerElement,LogicalVector3.Zero,out var Euler)){ return false; }
            if(Version>=2){ Euler=Euler*LogicalMath.TwoPi; }
            return LogicalQuaternion.TryFromEulerRuf(Euler,out Rotation);
        }
        var W=LogicalScalar.One;
        var Vector=LogicalVector3.Zero;
        if(!Element.TryGetProperty("R",out _)&&!Element.TryGetProperty("U",out _)&&!Element.TryGetProperty("F",out _)&&!Element.TryGetProperty("W",out _)){ return true; }
        if(!TryReadVector(Element,LogicalVector3.Zero,out Vector)||(Element.TryGetProperty("W",out var WElement)&&!TryReadScalar(WElement,out W))){ return false; }
        return new LogicalQuaternion(Vector.R,Vector.U,Vector.F,W).TryNormalize(out Rotation);
    }
    private bool TryReadVolume(JsonElement Members,int Version,LogicalAffineTransform World,string Scene,string Name,out string Error){
        Error=string.Empty;
        var Id=NavigationDefaults.Id;
        var Half=new LogicalVector3(LogicalScalar.FromInt64(10),LogicalScalar.FromInt64(5),LogicalScalar.FromInt64(10));
        var Radius=NavigationDefaults.AgentRadius;
        var Height=NavigationDefaults.AgentHeight;
        var Climb=NavigationDefaults.AgentMaxClimb;
        var Slope=NavigationDefaults.AgentMaxSlopeDegrees;
        var CellSize=NavigationDefaults.CellSize;
        var CellHeight=NavigationDefaults.CellHeight;
        string Problem(string Member)=>$"{Scene}: error NAV005: The NavMeshBounds of '{Name}' has an unreadable {Member}.";
        if(Members.ValueKind==JsonValueKind.Object){
            if(Members.TryGetProperty("Id",out var IdElement)){
                if(IdElement.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(IdElement.GetString())||!IsValidId(IdElement.GetString()!)){ Error=Problem("Id (letters, digits, '-', '_' and '.' only)"); return false; }
                Id=IdElement.GetString()!;
            }
            if(Members.TryGetProperty("HalfExtents",out var HalfElement)&&!TryReadVector(HalfElement,Half,out Half)){ Error=Problem("HalfExtents"); return false; }
            if(Members.TryGetProperty("AgentRadius",out var RadiusElement)&&!TryReadScalar(RadiusElement,out Radius)){ Error=Problem("AgentRadius"); return false; }
            if(Members.TryGetProperty("AgentHeight",out var HeightElement)&&!TryReadScalar(HeightElement,out Height)){ Error=Problem("AgentHeight"); return false; }
            if(Members.TryGetProperty("AgentMaxClimb",out var ClimbElement)&&!TryReadScalar(ClimbElement,out Climb)){ Error=Problem("AgentMaxClimb"); return false; }
            if(Members.TryGetProperty("CellSize",out var CellElement)&&!TryReadScalar(CellElement,out CellSize)){ Error=Problem("CellSize"); return false; }
            if(Members.TryGetProperty("CellHeight",out var CellHeightElement)&&!TryReadScalar(CellHeightElement,out CellHeight)){ Error=Problem("CellHeight"); return false; }
            // 角度: 版 3 は単位語なしのキーに回転数、版 2 は C# の名前のキーに回転数、版 1 は C# の名前のキーに度。
            var SlopeKey=Version>=3?"AgentMaxSlope":"AgentMaxSlopeDegrees";
            if(Members.TryGetProperty(SlopeKey,out var SlopeElement)){
                if(!TryReadScalar(SlopeElement,out Slope)){ Error=Problem(SlopeKey); return false; }
                if(Version>=2){ Slope=Slope*LogicalScalar.FromInt64(360); }
            }
        }
        if(Half.R<=LogicalScalar.Zero||Half.U<=LogicalScalar.Zero||Half.F<=LogicalScalar.Zero){ Error=Problem("HalfExtents (all three must be positive)"); return false; }
        var Corners = new LogicalVector3[8];
        for(var Index=0;Index<8;Index++){
            var Corner=new LogicalVector3((Index&1)==0?-Half.R:Half.R,(Index&2)==0?-Half.U:Half.U,(Index&4)==0?-Half.F:Half.F);
            if(!World.TryTransformPoint(Corner,out Corners[Index])){ Error=Problem("placement"); return false; }
        }
        var Low=Corners[0];
        var High=Corners[0];
        foreach(var Corner in Corners){
            Low=new LogicalVector3(Smaller(Low.R,Corner.R),Smaller(Low.U,Corner.U),Smaller(Low.F,Corner.F));
            High=new LogicalVector3(Larger(High.R,Corner.R),Larger(High.U,Corner.U),Larger(High.F,Corner.F));
        }
        this.Volumes.Add(new NavVolume(Id,Scene,Low,High,new NavMeshSettings(Radius,Height,Climb,Slope,CellSize,CellHeight)));
        return true;
    }
    private static LogicalScalar Smaller(LogicalScalar A,LogicalScalar B)=>A<B?A:B;
    private static LogicalScalar Larger(LogicalScalar A,LogicalScalar B)=>A>B?A:B;
    /// <summary>ファイル名になるので、英数字と <c>-_.</c> だけ。</summary>
    internal static bool IsValidId(string Id)=>Id.Length is >0 and <=64&&Id.All(static Character=>char.IsAsciiLetterOrDigit(Character)||Character is '-' or '_' or '.')&&Id[0]!='.';
    private bool TryAddMesh(string AssetPath,LogicalAffineTransform World,string Scene,string Name,out string Error){
        Error=string.Empty;
        var FullPath=Path.GetFullPath(Path.Combine(this.ProjectDirectory,AssetPath.Replace('/',Path.DirectorySeparatorChar)));
        if(!File.Exists(FullPath)){
            this.MissingMeshes.Add(AssetPath);
            return true;
        }
        if(!this.Meshes.TryGetValue(FullPath,out var Mesh)){
            var Result=GltfImporter.ImportScene(FullPath);
            if(!Result.Success||Result.Scene is not { }Imported){
                Error=$"{Scene}: error NAV006: The mesh '{AssetPath}' of '{Name}' could not be imported: {string.Join("; ",Result.Diagnostics)}";
                return false;
            }
            List<Vector3>Positions=[];
            List<int>Indices=[];
            foreach(var Item in Imported.Nodes){
                if(Item.MeshIndex is not { }MeshIndex){ continue; }
                var Data=Imported.Meshes[MeshIndex];
                var Offset=Positions.Count;
                foreach(var Position in Data.Positions){ Positions.Add(Vector3.Transform(Position,Item.WorldTransform)); }
                foreach(var Index in Data.Indices){ Indices.Add(Offset+(int)Index); }
            }
            Mesh=([.. Positions],[.. Indices]);
            this.Meshes[FullPath]=Mesh;
        }
        var Vertices=new LogicalVector3[Mesh.Positions.Length];
        for(var Index=0;Index<Vertices.Length;Index++){
            var Local=Mesh.Positions[Index];
            if(!LogicalScalar.TryFromSingle(Local.X,out var R)||!LogicalScalar.TryFromSingle(Local.Y,out var U)||!LogicalScalar.TryFromSingle(Local.Z,out var F)
                ||!World.TryTransformPoint(new LogicalVector3(R,U,F),out Vertices[Index])){
                Error=$"{Scene}: error NAV007: The mesh '{AssetPath}' of '{Name}' does not fit the coordinate range at its placement.";
                return false;
            }
        }
        // 鏡のような変換(負の拡縮)は巻き順を逆にするので、外向きの法線を保つために向きを戻す。
        var Mirrored=IsMirrored(World);
        for(var Index=0;Index+2<Mesh.Indices.Length;Index+=3){
            var A=Vertices[Mesh.Indices[Index]];
            var B=Vertices[Mesh.Indices[Index+1]];
            var C=Vertices[Mesh.Indices[Index+2]];
            if(!LogicalVector3.Cross(B-A,C-A).LengthSquared.IsZero){ this.Triangles.Add(Mirrored?new NavTriangle(A,C,B):new NavTriangle(A,B,C)); }
        }
        return true;
    }
    /// <summary>基底の向きが逆(行列式が負)か。</summary>
    internal static bool IsMirrored(LogicalAffineTransform World)=>
        LogicalVector3.Dot(World.RAxis,LogicalVector3.Cross(World.UAxis,World.FAxis))<LogicalScalar.Zero;
    private bool TryAddCollider(JsonElement Members,int Version,LogicalAffineTransform World,string Scene,string Name,out string Error){
        Error=string.Empty;
        if(Members.ValueKind==JsonValueKind.Object&&Members.TryGetProperty("IsTrigger",out var Trigger)&&Trigger.ValueKind==JsonValueKind.True){ return true; }
        var Shape=Members.ValueKind==JsonValueKind.Object&&Members.TryGetProperty("Shape",out var ShapeElement)&&ShapeElement.ValueKind==JsonValueKind.String?ShapeElement.GetString():"Box";
        if(Shape!="Box"){
            this.Messages.Add($"{Scene}: warning NAV008: The {Shape} collider of '{Name}' adds no navigation geometry (only boxes and static meshes are read).");
            return true;
        }
        var Center=LogicalVector3.Zero;
        var Half=new LogicalVector3(LogicalScalar.One/LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2));
        if(Members.ValueKind==JsonValueKind.Object){
            if((Members.TryGetProperty("Center",out var CenterElement)&&!TryReadVector(CenterElement,Center,out Center))
                ||(Members.TryGetProperty("HalfExtents",out var HalfElement)&&!TryReadVector(HalfElement,Half,out Half))){
                Error=$"{Scene}: error NAV005: The Collider of '{Name}' has an unreadable Center or HalfExtents.";
                return false;
            }
        }
        var Local=new LogicalVector3[8];
        var Corners=new LogicalVector3[8];
        for(var Index=0;Index<8;Index++){
            Local[Index]=Center+new LogicalVector3((Index&1)==0?-Half.R:Half.R,(Index&2)==0?-Half.U:Half.U,(Index&4)==0?-Half.F:Half.F);
            if(!World.TryTransformPoint(Local[Index],out Corners[Index])){
                Error=$"{Scene}: error NAV007: The Collider of '{Name}' does not fit the coordinate range at its placement.";
                return false;
            }
        }
        // 箱の 6 面(各面を 2 枚の三角形)。法線が外を向く巻き順にそろえる(上向き・下向きを見分けるため)。
        var Mirrored=IsMirrored(World);
        int[][]Faces=[[0,1,3,2],[4,5,7,6],[0,1,5,4],[2,3,7,6],[0,2,6,4],[1,3,7,5]];
        foreach(var Face in Faces){
            var FaceCenter=(Local[Face[0]]+Local[Face[1]]+Local[Face[2]]+Local[Face[3]])/LogicalScalar.FromInt64(4);
            var Normal=LogicalVector3.Cross(Local[Face[1]]-Local[Face[0]],Local[Face[2]]-Local[Face[0]]);
            var Reverse=(LogicalVector3.Dot(Normal,FaceCenter-Center)<LogicalScalar.Zero)!=Mirrored;
            int[]Order=Reverse?[Face[0],Face[3],Face[2],Face[1]]:Face;
            this.Triangles.Add(new NavTriangle(Corners[Order[0]],Corners[Order[1]],Corners[Order[2]]));
            this.Triangles.Add(new NavTriangle(Corners[Order[0]],Corners[Order[2]],Corners[Order[3]]));
        }
        return true;
    }
}
