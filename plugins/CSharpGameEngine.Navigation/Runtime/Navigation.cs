using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation;
/// <summary>
/// 読み込んだナビメッシュを持つサービス(ADR-0079/M0106)。System の引数に<c>Navigator</c>と書くと、同じゲームの全 System が同じ 1 つを受ける。
/// ナビメッシュは<see cref="NavMeshBounds"/>を持つ Entity が現れたときに読まれる(<c>NavigationLoad</c>)。
/// </summary>
[EngineService]
public sealed class Navigator {
    /// <summary><c>1</c>のとき、ナビメッシュを読まない(歩く者は直線で歩く)。ナビメッシュの有無での挙動の比較に使う。</summary>
    public const string DisableVariable="CGE_NAVIGATION_DISABLED";
    private readonly Dictionary<string,NavMesh>Meshes=new(StringComparer.Ordinal);
    private readonly List<NavMesh>Order=[];
    /// <summary>読み込み済みのナビメッシュの数。</summary>
    public int Count=>this.Order.Count;
    /// <summary>
    /// <c>Assets/Navigation/&lt;Id&gt;.cgenav</c>を読む。同じ<paramref name="Id"/>は 1 度だけ読む。ファイルが無い、または壊れていれば false と理由。
    /// </summary>
    public bool Load(string Id,out string Problem){
        Problem=string.Empty;
        if(this.Meshes.ContainsKey(Id)){ return true; }
        if(Environment.GetEnvironmentVariable(DisableVariable)=="1"){
            Problem=$"Navigation is switched off by {DisableVariable}=1; characters walk in straight lines.";
            return false;
        }
        var Path=ContentRoot.Resolve(NavigationDefaults.Folder+"/"+Id+NavigationDefaults.Extension);
        if(!File.Exists(Path)){
            Problem=$"The navigation mesh file was not found: {Path} (the build bakes it from the scene's NavMeshBounds).";
            return false;
        }
        byte[]Bytes;
        try { Bytes=File.ReadAllBytes(Path); }
        catch(Exception Error)when(Error is IOException or UnauthorizedAccessException){
            Problem=$"The navigation mesh file could not be read: {Error.Message}";
            return false;
        }
        if(!NavMesh.TryRead(Bytes,out var Mesh,out var Reason)){
            Problem=$"{Path}: {Reason}";
            return false;
        }
        this.Meshes[Id]=Mesh!;
        this.Order.Add(Mesh!);
        return true;
    }
    /// <summary>読み込み済みのナビメッシュを直接足す(テストと、ファイルを介さない使い方のため)。</summary>
    public void Add(string Id,NavMesh Mesh){
        if(this.Meshes.TryAdd(Id,Mesh)){ this.Order.Add(Mesh); }
    }
    /// <summary>
    /// 足の位置<paramref name="Start"/>から<paramref name="Goal"/>への経路。出発点が近くにあるナビメッシュ(読み込んだ順に最初のもの)で探す。どれにも無ければ false。
    /// </summary>
    public bool TryFindRoute(LogicalVector3 Start,LogicalVector3 Goal,out NavRoute Route){
        foreach(var Mesh in this.Order){
            if(Mesh.TryFindRoute(Start,Goal,out Route)){ return true; }
        }
        Route=new NavRoute([],false);
        return false;
    }
}
