using CSharpGameEngine.Navigation.Cook;
namespace CSharpGameEngine.Navigation.Tests;
/// <summary>テストの世界を作る道具: 三角形の床・箱・斜面、ナビメッシュの作成、覆っているかの判定、経路の長さ。</summary>
internal static class Geometry {
    public static LogicalVector3 P(float R,float U,float F)=>new(LogicalScalar.FromSingle(R),LogicalScalar.FromSingle(U),LogicalScalar.FromSingle(F));
    public static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    public static void Quad(List<NavTriangle>Into,LogicalVector3 A,LogicalVector3 B,LogicalVector3 C,LogicalVector3 D){
        Into.Add(new NavTriangle(A,B,C));
        Into.Add(new NavTriangle(A,C,D));
    }
    /// <summary>R と F が [Min, Max] の平らな床(上向き)。</summary>
    public static void Floor(List<NavTriangle>Into,float MinR,float MaxR,float MinF,float MaxF,float U=0f)=>
        Quad(Into,P(MinR,U,MinF),P(MinR,U,MaxF),P(MaxR,U,MaxF),P(MaxR,U,MinF));
    /// <summary>軸に平行な箱の 6 面(12 枚の三角形)。</summary>
    public static void Box(List<NavTriangle>Into,float MinR,float MinU,float MinF,float MaxR,float MaxU,float MaxF){
        Quad(Into,P(MinR,MaxU,MinF),P(MinR,MaxU,MaxF),P(MaxR,MaxU,MaxF),P(MaxR,MaxU,MinF));
        Quad(Into,P(MinR,MinU,MinF),P(MaxR,MinU,MinF),P(MaxR,MinU,MaxF),P(MinR,MinU,MaxF));
        Quad(Into,P(MinR,MinU,MinF),P(MinR,MaxU,MinF),P(MaxR,MaxU,MinF),P(MaxR,MinU,MinF));
        Quad(Into,P(MinR,MinU,MaxF),P(MaxR,MinU,MaxF),P(MaxR,MaxU,MaxF),P(MinR,MaxU,MaxF));
        Quad(Into,P(MinR,MinU,MinF),P(MinR,MinU,MaxF),P(MinR,MaxU,MaxF),P(MinR,MaxU,MinF));
        Quad(Into,P(MaxR,MinU,MinF),P(MaxR,MaxU,MinF),P(MaxR,MaxU,MaxF),P(MaxR,MinU,MaxF));
    }
    public static NavMeshSettings DefaultSettings=>new(NavigationDefaults.AgentRadius,NavigationDefaults.AgentHeight,NavigationDefaults.AgentMaxClimb,NavigationDefaults.AgentMaxSlopeDegrees,NavigationDefaults.CellSize,NavigationDefaults.CellHeight);
    /// <summary>領域 [-Half, Half] の R と F、U は [-1, 4] で作る。</summary>
    public static NavMesh Build(List<NavTriangle>Triangles,float Half=6f,NavMeshSettings?Settings=null){
        Assert.True(NavMeshBuilder.TryBuild(Settings??DefaultSettings,P(-Half,-1f,-Half),P(Half,4f,Half),Triangles,out var Mesh,out var Error),Error);
        return Mesh!;
    }
    /// <summary>(R, F)のセルを含む多角形で、床の高さが<paramref name="MaxFloor"/>以下のものがあるか(箱の上面など、高い面を数えない)。</summary>
    public static bool Covered(NavMesh Mesh,float R,float F,float MaxFloor=0.5f)=>CoveredBetween(Mesh,R,F,float.NegativeInfinity,MaxFloor);
    /// <summary>(R, F)のセルを含む多角形で、床の高さが [MinFloor, MaxFloor] のものがあるか。</summary>
    public static bool CoveredBetween(NavMesh Mesh,float R,float F,float MinFloor,float MaxFloor){
        var Cell=F_(Mesh.CellSize);
        var X=(int)MathF.Floor((R-F_(Mesh.Origin.R))/Cell);
        var Z=(int)MathF.Floor((F-F_(Mesh.Origin.F))/Cell);
        return Mesh.Polygons.Any(Polygon=>X>=Polygon.MinX&&X<Polygon.MaxX&&Z>=Polygon.MinZ&&Z<Polygon.MaxZ
            &&F_(Mesh.Origin.U)+Polygon.Y*F_(Mesh.CellHeight)>=MinFloor&&F_(Mesh.Origin.U)+Polygon.Y*F_(Mesh.CellHeight)<=MaxFloor);
    }
    private static float F_(LogicalScalar Value)=>F(Value);
    /// <summary>出発点から経路の点を順に結んだ水平の長さ。</summary>
    public static float Length(LogicalVector3 Start,NavRoute Route){
        var Total=0f;
        var Previous=Start;
        foreach(var Point in Route.Points){
            var Dr=F(Point.R)-F(Previous.R);
            var Df=F(Point.F)-F(Previous.F);
            Total+=MathF.Sqrt(Dr*Dr+Df*Df);
            Previous=Point;
        }
        return Total;
    }
    /// <summary>経路の線分を細かく歩いて、どの点もナビメッシュの多角形の中にあるか(線分が壁を通り抜けていないか)。</summary>
    public static bool StaysOnMesh(NavMesh Mesh,LogicalVector3 Start,NavRoute Route,float Step=0.05f,float MaxFloor=0.5f){
        var Previous=Start;
        foreach(var Point in Route.Points){
            var Dr=F(Point.R)-F(Previous.R);
            var Df=F(Point.F)-F(Previous.F);
            var Count=(int)MathF.Ceiling(MathF.Sqrt(Dr*Dr+Df*Df)/Step);
            for(var Index=0;Index<=Count;Index++){
                var T=Count==0?1f:(float)Index/Count;
                var R=F(Previous.R)+Dr*T;
                var Fw=F(Previous.F)+Df*T;
                // 折れ線の角は多角形の縁の上にあり、縁の上の点は隣のセルに数えられる。ごくわずかに内側の点も見る。
                const float Eps=0.01f;
                if(!(Covered(Mesh,R,Fw,MaxFloor)||Covered(Mesh,R+Eps,Fw,MaxFloor)||Covered(Mesh,R-Eps,Fw,MaxFloor)||Covered(Mesh,R,Fw+Eps,MaxFloor)||Covered(Mesh,R,Fw-Eps,MaxFloor))){ return false; }
            }
            Previous=Point;
        }
        return true;
    }
}
