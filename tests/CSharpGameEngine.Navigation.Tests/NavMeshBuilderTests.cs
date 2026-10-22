using CSharpGameEngine.Navigation.Cook;
namespace CSharpGameEngine.Navigation.Tests;
/// <summary>
/// ADR-0079/M0106: 三角形から歩ける面を作る。歩く者の大きさ(半径 0.35 m・背丈 1.44 m・段差 0.35 m・坂 44 度、格子 0.19 m × 0.1 m)の既定で、
/// 壁からの距離、隙間の幅、段差、斜面、天井の低さの境を、外から見える結果(多角形が覆うか、経路が通るか)で確かめる。
/// </summary>
public sealed class NavMeshBuilderTests {
    private static NavMesh FlatFloor(Action<List<NavTriangle>>?AddMore=null,float Half=6f){
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-Half-2,Half+2,-Half-2,Half+2);
        AddMore?.Invoke(Triangles);
        return Geometry.Build(Triangles,Half);
    }
    [Fact]
    public void AnEmptyScene_GivesAValidMeshWithNoPolygons(){
        var Mesh=Geometry.Build([]);
        Assert.Empty(Mesh.Polygons);
        Assert.Empty(Mesh.Portals);
        Assert.True(NavMesh.TryRead(Mesh.ToBytes(),out _,out _));
    }
    [Fact]
    public void OnlyVerticalWallsAndASteepSlope_HaveNothingToWalkOn(){
        List<NavTriangle>Triangles=[];
        Geometry.Quad(Triangles,Geometry.P(-4,0,0),Geometry.P(4,0,0),Geometry.P(4,3,0),Geometry.P(-4,3,0));
        // 60 度の斜面(44 度を超える)。
        Geometry.Quad(Triangles,Geometry.P(-4,0,2),Geometry.P(-4,0,5),Geometry.P(0,6.93f,5),Geometry.P(0,6.93f,2));
        Assert.Empty(Geometry.Build(Triangles).Polygons);
    }
    [Fact]
    public void AFlatFloor_IsOneRectangleInsetFromItsEdges(){
        var Mesh=FlatFloor();
        var Polygon=Assert.Single(Mesh.Polygons);
        Assert.Empty(Mesh.Portals);
        // 領域(12 m 四方)の縁の 1 セルは崖として外れ、さらに半径(0.35 m = 2 セル)ぶん離れる。両側で 3 セルずつ。
        var Cell=Geometry.F(Mesh.CellSize);
        Assert.InRange(Polygon.Width*Cell,12f-8*Cell,12f-4*Cell);
        Assert.InRange(Polygon.Depth*Cell,12f-8*Cell,12f-4*Cell);
        // 床の高さは、面を含むボクセルの上端(格子の段の高さ 0.1 m の丸め)。
        Assert.InRange(Geometry.F(Mesh.Origin.U)+Polygon.Y*Geometry.F(Mesh.CellHeight),0f,0.101f);
    }
    [Fact]
    public void AnObstacle_KeepsTheAgentAtLeastItsRadiusAwayAndNoFartherThanTwoCells(){
        var Mesh=FlatFloor(static Triangles=>Geometry.Box(Triangles,-1f,0f,-1f,1f,2f,1f));
        Assert.True(Mesh.Polygons.Count>1);
        // 箱の中と、半径(0.35 m)より近い所は覆われない。
        Assert.False(Geometry.Covered(Mesh,0f,0f));
        Assert.False(Geometry.Covered(Mesh,1.2f,0f));
        Assert.False(Geometry.Covered(Mesh,1.3f,0f));
        // 半径に 2 セル足した外は歩ける。
        Assert.True(Geometry.Covered(Mesh,1.9f,0f));
        Assert.True(Geometry.Covered(Mesh,0f,-1.9f));
        Assert.True(Geometry.Covered(Mesh,-3f,3f));
    }
    [Fact]
    public void ARouteAroundAnObstacle_IsLongerThanTheBlockedLineAndStaysOnTheMesh(){
        var Mesh=FlatFloor(static Triangles=>Geometry.Box(Triangles,-1f,0f,-1f,1f,2f,1f));
        var Start=Geometry.P(-4f,0f,0f);
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(4f,0f,0f),out var Route));
        Assert.False(Route.IsPartial);
        Assert.True(Route.Points.Count>=2,"The route must bend around the box.");
        var Length=Geometry.Length(Start,Route);
        Assert.InRange(Length,8.2f,10.5f);
        Assert.True(Geometry.StaysOnMesh(Mesh,Start,Route));
        // 終点は目的地。
        Assert.Equal(4f,Geometry.F(Route.Points[^1].R),0.2f);
        Assert.Equal(0f,Geometry.F(Route.Points[^1].F),0.2f);
    }
    [Fact]
    public void TheRoundTripOfARoute_HasTheSameLengthEitherWay(){
        var Mesh=FlatFloor(static Triangles=>Geometry.Box(Triangles,-1f,0f,-1f,1f,2f,1f));
        var A=Geometry.P(-4f,0f,0.3f);
        var B=Geometry.P(4f,0f,-0.4f);
        Assert.True(Mesh.TryFindRoute(A,B,out var There));
        Assert.True(Mesh.TryFindRoute(B,A,out var Back));
        Assert.Equal(Geometry.Length(A,There),Geometry.Length(B,Back),0.25f);
    }
    [Fact]
    public void AGapNarrowerThanTheAgent_CannotBePassedAndAWideOneCan(){
        NavMesh WithGap(float HalfGap)=>FlatFloor(Triangles=>{
            Geometry.Box(Triangles,-0.25f,0f,-8f,0.25f,2f,-HalfGap);
            Geometry.Box(Triangles,-0.25f,0f,HalfGap,0.25f,2f,8f);
        });
        // 幅 0.6 m(< 2 × (0.35 m + 余裕))。壁は領域の端まで続くので、向こう側へは行けない。
        var Narrow=WithGap(0.3f);
        Assert.True(Narrow.TryFindRoute(Geometry.P(-3f,0f,0f),Geometry.P(3f,0f,0f),out var Blocked));
        Assert.True(Blocked.IsPartial);
        Assert.True(Geometry.F(Blocked.Points[^1].R)<0f,"The route must stop on the near side of the wall.");
        // 幅 2 m は通れて、隙間の中を通る。
        var Wide=WithGap(1f);
        Assert.True(Wide.TryFindRoute(Geometry.P(-3f,0f,0f),Geometry.P(3f,0f,0f),out var Open));
        Assert.False(Open.IsPartial);
        Assert.True(Geometry.StaysOnMesh(Wide,Geometry.P(-3f,0f,0f),Open));
        Assert.Equal(6f,Geometry.Length(Geometry.P(-3f,0f,0f),Open),0.3f);
    }
    [Fact]
    public void AStepUpTheAgentCanClimbIsConnected_AndOneItCannotIsNot(){
        NavMesh WithStep(float Height)=>FlatFloor(Triangles=>Geometry.Box(Triangles,0f,-1f,-9f,9f,Height,9f));
        var Low=WithStep(0.2f);
        Assert.True(Low.TryFindRoute(Geometry.P(-3f,0f,0f),Geometry.P(3f,0.2f,0f),out var Over));
        Assert.False(Over.IsPartial);
        Assert.True(Geometry.F(Over.Points[^1].R)>2.5f);
        var High=WithStep(0.6f);
        Assert.True(High.TryFindRoute(Geometry.P(-3f,0f,0f),Geometry.P(3f,0.6f,0f),out var Blocked));
        Assert.True(Blocked.IsPartial);
        Assert.True(Geometry.F(Blocked.Points[^1].R)<0.5f);
    }
    [Fact]
    public void ASlopeWithinTheLimitIsWalkable_AndTheFloorBeyondItIsReachableOverIt(){
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-8f,-3f,-8f,8f);
        // 30 度: 5 m 進んで 2.89 m 上がる。
        Geometry.Quad(Triangles,Geometry.P(-3f,0f,-8f),Geometry.P(-3f,0f,8f),Geometry.P(2f,2.89f,8f),Geometry.P(2f,2.89f,-8f));
        Geometry.Floor(Triangles,2f,8f,-8f,8f,2.89f);
        var Mesh=Geometry.Build(Triangles,7f);
        var Start=Geometry.P(-5f,0f,0f);
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(5f,2.89f,0f),out var Route));
        Assert.False(Route.IsPartial);
        Assert.Equal(5f,Geometry.F(Route.Points[^1].R),0.3f);
        Assert.True(Geometry.StaysOnMesh(Mesh,Start,Route,MaxFloor:10f));
    }
    [Fact]
    public void AnOverheadSlab_BlocksTheSpaceUnderItOnlyWhenTheAgentCannotStandUp(){
        NavMesh WithSlab(float Bottom)=>FlatFloor(Triangles=>Geometry.Box(Triangles,0f,Bottom,-9f,9f,Bottom+0.2f,9f));
        // 下面が 1.0 m(背丈 1.44 m より低い)。
        var Low=WithSlab(1.0f);
        Assert.True(Low.TryFindRoute(Geometry.P(-3f,0f,0f),Geometry.P(3f,0f,0f),out var Blocked));
        Assert.True(Blocked.IsPartial);
        Assert.False(Geometry.Covered(Low,3f,0f));
        // 下面が 2.0 m なら下を通れる。
        var High=WithSlab(2.0f);
        Assert.True(High.TryFindRoute(Geometry.P(-3f,0f,0f),Geometry.P(3f,0f,0f),out var Open));
        Assert.False(Open.IsPartial);
        Assert.True(Geometry.Covered(High,3f,0f));
    }
    [Fact]
    public void ASurfaceAboveWithNoWayUp_IsAnIslandTheRouteDoesNotReach(){
        var Mesh=FlatFloor(static Triangles=>Geometry.Box(Triangles,2f,0f,-2f,5f,2f,2f));
        var Start=Geometry.P(-3f,0f,0f);
        // 箱の上面(高さ 2 m)は平らで歩けるが、登れない。そこへは着けず、行ける中で一番近い点までの経路になる。
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(3.5f,2f,0f),out var Route));
        Assert.True(Route.IsPartial);
        Assert.True(Geometry.F(Route.Points[^1].U)<1f,"The nearest reachable point is on the floor, not on top of the box.");
        Assert.InRange(Geometry.F(Route.Points[^1].R),0.5f,1.9f);
    }
    [Fact]
    public void TheFloorInsideAClosedBoxThatRestsOnIt_IsNotWalkable_ButAThinPlateIsStillStoodOn(){
        // 箱の底面が床の上面と同じ高さで重なる: 箱の内側(高さ 2 m の空洞)に床の面が残るが、そこへは行けず立てもしない。
        var Mesh=FlatFloor(static Triangles=>Geometry.Box(Triangles,-1f,0f,-1f,1f,2f,1f));
        Assert.False(Geometry.Covered(Mesh,0f,0f,MaxFloor:0.5f));
        // 床から浮いた厚さ 0.1 m の板(底面が上面より下)の上面は歩ける: TopDown の中央の台がこの形。
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-8f,8f,-8f,8f);
        Geometry.Box(Triangles,-2f,2f,-2f,2f,2.1f,2f);
        var Plate=Geometry.Build(Triangles);
        Assert.True(Geometry.CoveredBetween(Plate,0f,0f,1.9f,2.3f),"The top of the plate must be walkable.");
        // 板の下の床も、頭上に 2 m あるので歩ける。
        Assert.True(Geometry.Covered(Plate,0f,0f,MaxFloor:0.5f));
    }
    [Fact]
    public void AFloorWhoseTrianglesFaceDown_IsStillWalkable(){
        // 外向きの巻き順を前提にするのは、閉じた形の内側の床を見分けるときだけ。向きが逆の 1 枚の床(物理は両面)も歩ける。
        List<NavTriangle>Triangles=[];
        Geometry.Quad(Triangles,Geometry.P(-8f,0f,-8f),Geometry.P(8f,0f,-8f),Geometry.P(8f,0f,8f),Geometry.P(-8f,0f,8f));
        Assert.Single(Geometry.Build(Triangles).Polygons);
    }
    [Fact]
    public void TwoBuilds_GiveTheSameBytes(){
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-8f,8f,-8f,8f);
        Geometry.Box(Triangles,-1f,0f,-1f,1f,2f,1f);
        Geometry.Box(Triangles,3f,0f,-4f,4f,1f,4f);
        Geometry.Quad(Triangles,Geometry.P(-6f,0f,2f),Geometry.P(-6f,0f,5f),Geometry.P(-4f,1f,5f),Geometry.P(-4f,1f,2f));
        Assert.Equal(Geometry.Build(Triangles).ToBytes(),Geometry.Build([.. Triangles]).ToBytes());
    }
    [Fact]
    public void AVolumeTooLargeForItsCellSize_IsRefusedWithAClearMessage(){
        Assert.False(NavMeshBuilder.TryBuild(Geometry.DefaultSettings,Geometry.P(-1000,0,-1000),Geometry.P(1000,10,1000),[],out var Mesh,out var Error));
        Assert.Null(Mesh);
        Assert.Contains("too large",Error);
        Assert.False(NavMeshBuilder.TryBuild(Geometry.DefaultSettings,Geometry.P(0,0,0),Geometry.P(0,10,5),[],out _,out var Flat));
        Assert.Contains("no size",Flat);
    }
}
