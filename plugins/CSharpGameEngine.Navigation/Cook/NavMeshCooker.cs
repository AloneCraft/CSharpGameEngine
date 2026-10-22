using CSharpGameEngine.Cooking;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation.Cook;
/// <summary>
/// シーン(JSON)の静的な衝突形状から、ナビメッシュ<c>Assets/Navigation/&lt;Id&gt;.cgenav</c>を作る(ADR-0079/M0106)。
/// 入力はシーン 1 つ。そのシーンの<c>NavMeshBounds</c>(UE の NavMeshBoundsVolume に当たる)が領域と歩く者の大きさを決め、衝突形状は、プロジェクトの
/// <c>Scenes</c>にある起動シーン(<c>startupOrder</c>つき)全部から取る。同じ<c>Id</c>の領域は 1 つのナビメッシュに合わせ(領域の和)、<c>Id</c>ごとに 1 ファイルを書く。
/// 失敗と警告は<c>ファイル: error NAVnnn: 内容</c>の形で返す(MSBuild の Exec がそのままビルドの診断にする)。
/// </summary>
public sealed class NavMeshCooker:IAssetCooker {
    public string Name=>"NavMesh";
    public IReadOnlyList<string> InputExtensions=>[".json"];
    public CookResult Cook(CookRequest Request){
        var SceneName=Path.GetFileName(Request.InputPath);
        if(!SceneGeometry.TryLoad(Request.InputPath,Request.ProjectDirectory,out var Geometry,out var LoadError)){ return CookResult.Failure(LoadError); }
        List<string>Messages=[.. Geometry.Messages];
        if(Geometry.Volumes.Count==0){
            return CookResult.Failure($"{SceneName}: error NAV009: The scene has no {SceneGeometry.BoundsType}, so there is no navigation volume to bake.");
        }
        List<string>Outputs=[];
        foreach(var Group in Geometry.Volumes.GroupBy(static Volume=>Volume.Id,StringComparer.Ordinal).OrderBy(static Group=>Group.Key,StringComparer.Ordinal)){
            var Volumes=Group.ToList();
            var Settings=Volumes[0].Settings;
            if(Volumes.Any(Volume=>Volume.Settings!=Settings)){
                return CookResult.Failure($"{SceneName}: error NAV010: The NavMeshBounds with Id '{Group.Key}' disagree on the agent or cell settings; give the volumes of one navigation mesh the same values.");
            }
            var Min=Volumes[0].Min;
            var Max=Volumes[0].Max;
            foreach(var Volume in Volumes.Skip(1)){
                Min=new LogicalVector3(Smaller(Min.R,Volume.Min.R),Smaller(Min.U,Volume.Min.U),Smaller(Min.F,Volume.Min.F));
                Max=new LogicalVector3(Larger(Max.R,Volume.Max.R),Larger(Max.U,Volume.Max.U),Larger(Max.F,Volume.Max.F));
            }
            if(!NavMeshBuilder.TryBuild(Settings,Min,Max,Geometry.Triangles,out var Mesh,out var BuildError)){
                return CookResult.Failure($"{SceneName}: error NAV011: The navigation mesh '{Group.Key}' could not be built: {BuildError}");
            }
            var Relative=NavigationDefaults.Folder+"/"+Group.Key+NavigationDefaults.Extension;
            var Destination=Path.Combine(Request.OutputDirectory,Relative.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.WriteAllBytes(Destination,Mesh!.ToBytes());
            Outputs.Add(Relative);
            Messages.Add(Mesh.Polygons.Count==0
                ?$"{SceneName}: warning NAV012: The navigation mesh '{Group.Key}' has no walkable surface; the volume holds no collision a character can stand on."
                :$"{SceneName}: NavMesh '{Group.Key}': {Mesh.Polygons.Count} polygons, {Mesh.Portals.Count} portals, {Mesh.GridWidth}x{Mesh.GridDepth} cells.");
        }
        return new CookResult(true,Outputs,Messages);
    }
    private static LogicalScalar Smaller(LogicalScalar A,LogicalScalar B)=>A<B?A:B;
    private static LogicalScalar Larger(LogicalScalar A,LogicalScalar B)=>A>B?A:B;
}
