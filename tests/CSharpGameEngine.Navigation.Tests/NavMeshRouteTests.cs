namespace CSharpGameEngine.Navigation.Tests;
/// <summary>ADR-0079/M0106: ナビメッシュの上の経路探索(A* と funnel)。同じ多角形の中、回り込み、左右の対称、着けない目的地、出発点がメッシュの外、決定性。</summary>
public sealed class NavMeshRouteTests {
    private static NavMesh BoxInTheMiddle(){
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-8f,8f,-8f,8f);
        Geometry.Box(Triangles,-1f,0f,-1f,1f,2f,1f);
        return Geometry.Build(Triangles);
    }
    [Fact]
    public void WithinOnePolygon_TheRouteIsTheGoalItself(){
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-8f,8f,-8f,8f);
        var Mesh=Geometry.Build(Triangles);
        var Start=Geometry.P(-3f,0f,-2f);
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(4f,0f,3f),out var Route));
        Assert.False(Route.IsPartial);
        var Point=Assert.Single(Route.Points);
        Assert.Equal(4f,Geometry.F(Point.R),0.2f);
        Assert.Equal(3f,Geometry.F(Point.F),0.2f);
    }
    [Fact]
    public void TheRouteBendsToTheSideThatIsShorter_ForBothTurnDirections(){
        var Mesh=BoxInTheMiddle();
        Assert.True(Mesh.TryFindRoute(Geometry.P(-4f,0f,0.4f),Geometry.P(4f,0f,0.4f),out var Up));
        Assert.True(Up.Points.Count>=2);
        Assert.All(Up.Points.Take(Up.Points.Count-1),static Point=>Assert.True(Geometry.F(Point.F)>0.5f,"The corners must be on the +F side of the box."));
        Assert.True(Mesh.TryFindRoute(Geometry.P(-4f,0f,-0.4f),Geometry.P(4f,0f,-0.4f),out var Down));
        Assert.All(Down.Points.Take(Down.Points.Count-1),static Point=>Assert.True(Geometry.F(Point.F)<-0.5f,"The corners must be on the -F side of the box."));
        // 逆向きも同じ側を回る(左右の取り違えがあると、角が箱の向こう側になる)。
        Assert.True(Mesh.TryFindRoute(Geometry.P(4f,0f,0.4f),Geometry.P(-4f,0f,0.4f),out var Back));
        Assert.All(Back.Points.Take(Back.Points.Count-1),static Point=>Assert.True(Geometry.F(Point.F)>0.5f));
    }
    [Fact]
    public void TheCornersOfARoute_HugTheObstacleAtTheAgentsDistance(){
        var Mesh=BoxInTheMiddle();
        Assert.True(Mesh.TryFindRoute(Geometry.P(-4f,0f,0.4f),Geometry.P(4f,0f,0.4f),out var Route));
        // 角は箱の角(1, 1)から、半径 + 余裕(0.35〜0.6 m)だけ離れた所にある。
        var Corner=Route.Points[0];
        var Distance=MathF.Sqrt((Geometry.F(Corner.R)+1f)*(Geometry.F(Corner.R)+1f)+(Geometry.F(Corner.F)-1f)*(Geometry.F(Corner.F)-1f));
        Assert.InRange(Distance,0.35f,0.75f);
    }
    [Fact]
    public void AnUnreachableGoal_GivesAPartialRouteToTheNearestReachablePoint(){
        var Mesh=BoxInTheMiddle();
        var Start=Geometry.P(-5f,0f,0f);
        // 箱の真ん中(どの多角形からも半径の 2 倍より遠い): 一番近い行ける点は箱のそば。
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(0f,0f,0f),out var Inside));
        Assert.True(Inside.IsPartial);
        var End=Inside.Points[^1];
        Assert.InRange(MathF.Sqrt(Geometry.F(End.R)*Geometry.F(End.R)+Geometry.F(End.F)*Geometry.F(End.F)),1.2f,2.6f);
        // 領域の外。
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(30f,0f,0f),out var Outside));
        Assert.True(Outside.IsPartial);
        Assert.InRange(Geometry.F(Outside.Points[^1].R),4f,7.5f);
    }
    [Fact]
    public void AStartFarFromTheMesh_HasNoRoute(){
        var Mesh=BoxInTheMiddle();
        Assert.False(Mesh.TryFindRoute(Geometry.P(40f,0f,40f),Geometry.P(0f,0f,5f),out var Route));
        Assert.Empty(Route.Points);
        // 床より背丈を超えて高い所(空中)も、ナビメッシュの上ではない。
        Assert.False(Mesh.TryFindRoute(Geometry.P(-4f,5f,0f),Geometry.P(0f,0f,5f),out _));
    }
    [Fact]
    public void AStartTouchingTheWall_IsMovedOntoTheMesh(){
        var Mesh=BoxInTheMiddle();
        // 壁から 0.2 m(半径より近い): そのままでは多角形の外だが、近くの多角形へ寄せて経路が引ける。
        var Start=Geometry.P(1.2f,0f,0f);
        Assert.False(Geometry.Covered(Mesh,1.2f,0f));
        Assert.True(Mesh.TryFindRoute(Start,Geometry.P(4f,0f,4f),out var Route));
        Assert.False(Route.IsPartial);
        Assert.True(Mesh.TryLocate(Start,out var Polygon,out var Projected));
        Assert.InRange(Polygon,0,Mesh.Polygons.Count-1);
        Assert.True(Geometry.F(Projected.R)>=1.2f);
    }
    [Fact]
    public void TheSameQuery_GivesTheSameRouteEveryTime(){
        var Mesh=BoxInTheMiddle();
        Assert.True(Mesh.TryFindRoute(Geometry.P(-4f,0f,0.4f),Geometry.P(4f,0f,0.9f),out var A));
        Assert.True(Mesh.TryFindRoute(Geometry.P(-4f,0f,0.4f),Geometry.P(4f,0f,0.9f),out var B));
        Assert.Equal(A.Points,B.Points);
        Assert.Equal(A.IsPartial,B.IsPartial);
    }
    [Fact]
    public void ARouteOverARamp_CarriesTheFloorHeightOfEachPolygon(){
        List<NavTriangle>Triangles=[];
        Geometry.Floor(Triangles,-8f,-3f,-8f,8f);
        Geometry.Quad(Triangles,Geometry.P(-3f,0f,-8f),Geometry.P(-3f,0f,8f),Geometry.P(2f,2.89f,8f),Geometry.P(2f,2.89f,-8f));
        Geometry.Floor(Triangles,2f,8f,-8f,8f,2.89f);
        var Mesh=Geometry.Build(Triangles,7f);
        Assert.True(Mesh.TryFindRoute(Geometry.P(-5f,0f,0f),Geometry.P(5f,2.89f,0f),out var Route));
        Assert.InRange(Geometry.F(Route.Points[^1].U),2.8f,3.1f);
    }
}
