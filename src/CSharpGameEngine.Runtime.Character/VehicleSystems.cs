using System.Runtime.CompilerServices;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// 車を1固定tickに1回進める。キャラクターと同じく、物理の更新の後で静的なコリジョンが同期されてから動く。
/// 車は根のノードで、Transformの位置は車体の中心(物理の座標系)。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsTransformWriteback))]
internal struct VehicleMotionUpdate:ISystem {
    private static readonly ConditionalWeakTable<DeterministicPhysicsWorld,VehicleMotor>Motors=[];
    public void Update(ref Transform Transform,ref Vehicle Settings,ref VehicleState Car,in VehicleIntent Intent,IPhysicsHost Host,ref State State){
        if(Transform.Frame!=Host.Physics.Frame){
            return;
        }
        var Motor=Motors.GetValue(Host.Physics.Active.MutableWorld,static Key=>new VehicleMotor(Key));
        var DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        var Position=Transform.Position;
        var Rotation=Transform.Rotation;
        try {
            Motor.Step(Settings,ref Position,ref Rotation,ref Car,Intent,DeltaSeconds);
        }
        catch(OverflowException){
            // 数値が収まらない設定のときは、車はそのtickの間そこに留まる。
            return;
        }
        Transform.Position=Position;
        Transform.Rotation=Rotation;
    }
}
/// <summary>
/// 追従カメラの向きを、車の進む向きへなだらかに回す。車の子の<c>OrbitCamera</c>は、根の<see cref="ViewAngles"/>の向きで車の後ろに置かれるので、
/// ここでその Yaw を車の向きに寄せる。物理の後、カメラを置く前に動く。Pitch は書かない(シーンの値のまま)。
/// </summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct VehicleChaseView:ISystem {
    /// <summary>向きに追いつく速さ(1/s)。大きいほどカメラが素早く車の後ろへ回る。</summary>
    private const float FollowRate=5f;
    public void Update(in Transform Transform,ref ViewAngles View,in VehicleState Car,ref State State){
        VehicleMotor.Basis(Transform.Rotation,out var Forward,out _,out _);
        if(!Forward.R.TryToSingle(out var R)||!Forward.F.TryToSingle(out var F)||(R==0f&&F==0f)){
            return;
        }
        var Target=MathF.Atan2(R,F)*(180f/MathF.PI);
        var Difference=(Target-View.YawDegrees)%360f;
        if(Difference>180f){
            Difference-=360f;
        }
        else if(Difference<-180f){
            Difference+=360f;
        }
        View.YawDegrees+=Difference*MathF.Min(1f,FollowRate*State.FrameDeltaSeconds);
    }
}
