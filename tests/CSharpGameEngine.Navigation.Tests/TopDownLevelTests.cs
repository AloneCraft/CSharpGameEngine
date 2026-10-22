using CSharpGameEngine.Cooking;
using CSharpGameEngine.Navigation.Cook;
namespace CSharpGameEngine.Navigation.Tests;
/// <summary>
/// ADR-0079/M0106: UE5.8 の TopDown のレベル(中央の台とスロープ、壁、角の建物)を、テンプレートのシーンと glb から実際に焼く。
/// 歩ける領域と経路が、レベルの形に合っていることを固定する。
/// </summary>
public sealed class TopDownLevelTests:IDisposable {
    private static readonly string Template=Path.Combine(TestProject.RepositoryRoot,"Templates","CSharpGameEngine.Templates.TopDown");
    private readonly string Output=Path.Combine(Path.GetTempPath(),"cge-nav-topdown-"+Guid.NewGuid().ToString("N"));
    public void Dispose(){
        if(Directory.Exists(this.Output)){ Directory.Delete(this.Output,recursive:true); }
    }
    private NavMesh Bake(string Folder="a"){
        var Directory_=Path.Combine(this.Output,Folder);
        Directory.CreateDirectory(Directory_);
        var Result=new NavMeshCooker().Cook(new CookRequest(Path.Combine(Template,"Scenes","TopDownNavigation.json"),Directory_,Template));
        Assert.True(Result.Succeeded,string.Join(" ",Result.Messages));
        Assert.True(NavMesh.TryRead(File.ReadAllBytes(Path.Combine(Directory_,"Assets","Navigation","Lvl_TopDown.cgenav")),out var Mesh,out var Error),Error);
        return Mesh!;
    }
    [Fact]
    public void TheLevelBakesWithUnrealsAgentAndCellValues_AndDeterministically(){
        var Mesh=this.Bake("a");
        // UE5.8 TopDown の RecastNavMesh: 半径 35 cm、背丈 144 cm、段差 35 cm、坂 44 度、格子 19 cm × 10 cm。領域は ±22.5 m × ±3.75 m。
        Assert.Equal(0.35f,Geometry.F(Mesh.Settings.AgentRadius),0.001f);
        Assert.Equal(1.44f,Geometry.F(Mesh.Settings.AgentHeight),0.001f);
        Assert.Equal(0.35f,Geometry.F(Mesh.Settings.AgentMaxClimb),0.001f);
        Assert.Equal(44f,Geometry.F(Mesh.Settings.AgentMaxSlopeDegrees),0.001f);
        Assert.Equal(0.19f,Geometry.F(Mesh.CellSize),0.001f);
        Assert.Equal(0.1f,Geometry.F(Mesh.CellHeight),0.001f);
        Assert.Equal(-22.5f,Geometry.F(Mesh.Origin.R),0.001f);
        Assert.Equal(-3.75f,Geometry.F(Mesh.Origin.U),0.001f);
        Assert.Equal(237,Mesh.GridWidth);
        Assert.InRange(Mesh.Polygons.Count,300,1500);
        Assert.NotEmpty(Mesh.Portals);
        Assert.Equal(Mesh.ToBytes(),this.Bake("b").ToBytes());
    }
    [Fact]
    public void FromTheCentralPlatform_TheRouteToTheFloorGoesDownARampInsteadOfOffTheEdge(){
        var Mesh=this.Bake();
        var Start=Geometry.P(0f,2.05f,0f);
        var Goal=Geometry.P(7f,0f,7f);
        // 一直線は台のふち(2 m の崖)を越えるので、ナビメッシュの上には無い。
        Assert.False(Geometry.StaysOnMesh(Mesh,Start,new NavRoute([Goal],false),MaxFloor:10f));
        Assert.True(Mesh.TryFindRoute(Start,Goal,out var Route));
        Assert.False(Route.IsPartial);
        Assert.True(Geometry.StaysOnMesh(Mesh,Start,Route,MaxFloor:10f));
        var Straight=MathF.Sqrt(98f);
        Assert.InRange(Geometry.Length(Start,Route),Straight*1.1f,Straight*2f);
        // スロープを下る: 最初の角はスロープの上の端(台の高さ 2 m)、最後は床。
        var Heights=Route.Points.Select(static Point=>Geometry.F(Point.U)).ToArray();
        Assert.InRange(Heights[0],1.9f,2.2f);
        Assert.InRange(Heights[^1],-0.05f,0.15f);
    }
    [Fact]
    public void TheTopOfACornerBuilding_IsReachedUpItsRamp(){
        var Mesh=this.Bake();
        // 角の建物(高さ 2 m の箱)の上の点。建物にはスロープが付いているので、経路は床からスロープを登って、上面の目的地に着く。
        var Start=Geometry.P(6.68f,0.05f,8.13f);
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(15f,2f,12f),out var Route));
        Assert.False(Route.IsPartial);
        Assert.InRange(Geometry.F(Route.Points[^1].U),1.9f,2.3f);
        Assert.True(Geometry.StaysOnMesh(Mesh,Start,Route,MaxFloor:10f));
    }
    [Fact]
    public void AGoalAcrossTheLevel_NeedsARouteMuchLongerThanTheStraightLine(){
        var Mesh=this.Bake();
        var Start=Geometry.P(6.68f,0.05f,8.13f);
        // 中央の台とスロープの壁に遮られて、床を回るか台を越える経路になる。
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(-6f,0.05f,-8f),out var Route));
        Assert.False(Route.IsPartial);
        var Straight=MathF.Sqrt((6.68f+6f)*(6.68f+6f)+(8.13f+8f)*(8.13f+8f));
        Assert.True(Geometry.Length(Start,Route)>Straight*1.15f);
        Assert.True(Geometry.StaysOnMesh(Mesh,Start,Route,MaxFloor:10f));
    }
}
