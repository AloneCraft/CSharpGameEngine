using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
namespace CSharpGameEngine.Navigation;
/// <summary>
/// <see cref="NavMeshBounds"/>を持つ Entity が現れたら、そのナビメッシュを読む(1 度だけ)。読めなければ理由を標準エラーへ 1 回出し、
/// <see cref="NavMeshBoundsRuntime.Problem"/>に残す。そのとき歩く者は従来どおり直線で歩く。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsBefore(typeof(NavigationSteering))]
internal struct NavigationLoad:ISystem {
    public void Update(in NavMeshBounds Bounds,ref NavMeshBoundsRuntime Runtime,Navigator Navigator){
        if(Runtime.Attempted){ return; }
        Runtime.Attempted=true;
        Runtime.Loaded=Navigator.Load(Bounds.Id,out var Problem);
        if(!Runtime.Loaded){
            Runtime.Problem=Problem;
            Console.Error.WriteLine("CGE-NAVIGATION "+Problem);
        }
    }
}
/// <summary>
/// <c>MoveIntent.MoveTo</c>の目的地への経路を、ナビメッシュで探して追う(ADR-0079/M0106)。キャラクターの移動(<c>CharacterMotionUpdate</c>)の前に、
/// 経路の次の経由点を<see cref="MoveIntent.Waypoint"/>へ置く。途中の点は通過点(最高速度のまま、<see cref="NavAgent.WaypointRadius"/>に入ったら次へ)、最後の点は減速して止まる点。
/// 経路が目的地に着けないとき(<see cref="NavRoute.IsPartial"/>)は、UE の SimpleMoveToLocation と同じく、行ける中で一番近い点まで歩いて止まる。ナビメッシュが無い、または出発点が外なら何もせず、直線で歩く。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsBefore(typeof(CharacterMotionUpdate))]
internal struct NavigationSteering:ISystem {
    public void Update(in Transform Transform,in CharacterMotion Motion,ref MoveIntent Intent,in NavAgent Agent,ref NavAgentState Walk,Navigator Navigator){
        if(!Intent.HasDestination){
            Walk.Route=null;
            Walk.Planned=false;
            Intent.HasWaypoint=false;
            return;
        }
        var Feet=Transform.Position-new LogicalVector3(LogicalScalar.Zero,Motion.CapsuleHalfHeight,LogicalScalar.Zero);
        var Goal=Intent.Destination;
        if(!Walk.Planned||PlanarDistanceSquared(Goal,Walk.Goal)>Agent.GoalTolerance*Agent.GoalTolerance||OffRoute(Feet,Agent,Walk)){
            Walk.Planned=true;
            Walk.Goal=Goal;
            Walk.Origin=Feet;
            Walk.Next=0;
            Walk.Route=Navigator.TryFindRoute(Feet,Goal,out var Route)?Route:null;
        }
        if(Walk.Route is not { }Current||Current.Points.Count==0){
            Intent.HasWaypoint=false;
            return;
        }
        var Last=Current.Points.Count-1;
        while(Walk.Next<Last&&PlanarDistanceSquared(Feet,Current.Points[Walk.Next])<=Agent.WaypointRadius*Agent.WaypointRadius){ Walk.Next++; }
        // 最後の点は、減速して止まる目的地として歩く(WaypointIsEnd)。着ける経路では目的地を床へ寄せた点、着けない経路(IsPartial)では行ける中で一番近い点。
        // 目的地そのものへ向けると、壁の中やナビメッシュの外の目的地で、壁を押し続けてしまう。
        Intent.Waypoint=Current.Points[Walk.Next];
        Intent.HasWaypoint=true;
        Intent.WaypointIsEnd=Walk.Next>=Last;
    }
    /// <summary>歩いている区間(出発点から次の経由点)から水平に<see cref="NavAgent.ReplanDistance"/>以上離れた。</summary>
    private static bool OffRoute(LogicalVector3 Feet,in NavAgent Agent,in NavAgentState Walk){
        if(Walk.Route is not { }Current||Current.Points.Count==0||Walk.Next>=Current.Points.Count){ return false; }
        var From=Walk.Next==0?Walk.Origin:Current.Points[Walk.Next-1];
        var To=Current.Points[Walk.Next];
        var Along=new LogicalVector3(To.R-From.R,LogicalScalar.Zero,To.F-From.F);
        var Offset=new LogicalVector3(Feet.R-From.R,LogicalScalar.Zero,Feet.F-From.F);
        var Length=Along.LengthSquared;
        var Fraction=Length.IsZero?LogicalScalar.Zero:LogicalVector3.Dot(Offset,Along)/Length;
        Fraction=Fraction<LogicalScalar.Zero?LogicalScalar.Zero:Fraction>LogicalScalar.One?LogicalScalar.One:Fraction;
        var Closest=Along*Fraction;
        return (Offset-Closest).LengthSquared>Agent.ReplanDistance*Agent.ReplanDistance;
    }
    private static LogicalScalar PlanarDistanceSquared(LogicalVector3 A,LogicalVector3 B){
        var Dr=A.R-B.R;
        var Df=A.F-B.F;
        return Dr*Dr+Df*Df;
    }
}
