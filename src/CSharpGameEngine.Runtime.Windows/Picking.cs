using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Input;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>ポインターの下で、最初に当たった物(ADR-0077)。<see cref="Distance"/>はカメラからの距離(m)。</summary>
public readonly record struct PickHit(Entity Entity,LogicalVector3 Point,LogicalVector3 Normal,float Distance);
/// <summary>
/// 画面上のポインター(<see cref="Pointer"/>)が指す3Dの位置を求める入口。System の引数に <c>Picking Picking</c> と書くと、
/// 生成されたループが <c>EngineHost.Picking</c> を渡す。カメラからポインターを通る光線を、物理の静的コリジョンへ飛ばして、
/// 最も近い当たりを返す。TopDown の「クリックした地面へ歩く」はこれで書く。
/// </summary>
public sealed class Picking {
    private readonly EngineHost Host;
    private readonly List<PhysicsRayHit>Hits=[];
    internal Picking(EngineHost Host){
        this.Host=Host;
    }
    /// <summary>光線の長さ(m)。これより遠い物は当たらない。</summary>
    public const int MaxDistanceMeters=2000;
    /// <summary>
    /// カメラの姿勢と画角から、画面の点(左上が0、右下が1の割合)を通る光線を作る。画角は縦の視野角(ラジアン)で、
    /// <paramref name="AspectRatio"/>は幅÷高さ。数値が収まらないときは false。
    /// </summary>
    internal static bool TryCreateRay(in LogicalAffineTransform Camera,float FovYRadians,float AspectRatio,float X,float Y,out PhysicsRay Ray){
        Ray=default;
        if(!(FovYRadians>0f&&FovYRadians<MathF.PI)||!(AspectRatio>0f)||!float.IsFinite(X)||!float.IsFinite(Y)){ return false; }
        var Half=MathF.Tan(FovYRadians*0.5f);
        var Local=new Vector3((X*2f-1f)*Half*AspectRatio,(1f-Y*2f)*Half,1f);
        if(!Camera.RAxis.TryToSingle(out var Right)||!Camera.UAxis.TryToSingle(out var Up)||!Camera.FAxis.TryToSingle(out var Forward)){ return false; }
        var Direction=Vector3.Normalize(Right*Local.X+Up*Local.Y+Forward*Local.Z);
        if(!LogicalScalar.TryFromSingle(Direction.X,out var DirectionR)||!LogicalScalar.TryFromSingle(Direction.Y,out var DirectionU)||!LogicalScalar.TryFromSingle(Direction.Z,out var DirectionF)){ return false; }
        Ray=new PhysicsRay(Camera.Translation,new LogicalVector3(DirectionR,DirectionU,DirectionF),LogicalScalar.FromInt64(MaxDistanceMeters));
        return true;
    }
    /// <summary>
    /// <paramref name="Pointer"/>の下の物を求める。ポインターが領域の外、カメラが無い、カメラと物理の座標系が違う、
    /// 何にも当たらないときは false。コリジョンを持つ(<c>StaticMeshCollider</c>などの)物だけが当たる。
    /// </summary>
    public bool TryPick(Pointer Pointer,out PickHit Hit){
        Hit=default;
        if(!Pointer.Inside||!this.Host.TryPointerRay(Pointer.X,Pointer.Y,out var Ray)){ return false; }
        this.Hits.Clear();
        var Result=this.Host.PhysicsWorld.Raycast(in Ray,PhysicsLayerMask.All,this.Hits);
        if(Result.Status!=PhysicsQueryStatus.Success||this.Hits.Count==0){ return false; }
        var Nearest=this.Hits[0];
        foreach(var Candidate in this.Hits){
            if(Candidate.Distance<Nearest.Distance){ Nearest=Candidate; }
        }
        Hit=new PickHit(Nearest.Entity,Nearest.Point,Nearest.Normal,Nearest.Distance.TryToSingle(out var Distance)?Distance:0f);
        return true;
    }
}
