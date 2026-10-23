using System.Runtime.CompilerServices;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Character;
namespace CSharpGameEngine.Vehicles;
/// <summary>
/// 車の剛体を物理より先に整える(ADR-0078/M0109)。最初の tick に剛体の向き(<c>PhysicsRotation</c>)を Transform の向きにする(生成した場所の向きで
/// 剛体が作られるように)。設定があれば剛体の質量を設定の質量にする(UE の UpdateMassProperties)。
/// </summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct VehicleBodySetup:ISystem {
    public void Update(ref Transform Transform,ref Vehicle Vehicle,ref VehicleState Car,ref RigidBody Body,ref PhysicsRotation Rotation){
        if(!Car.Initialized){
            Rotation.Value=Transform.Rotation;
            Car.Initialized=true;
        }
        if(Vehicle.Setup is { }Setup&&Body.Mass!=Setup.Mass&&Setup.Mass>LogicalScalar.Zero){
            Body.Mass=Setup.Mass;
        }
    }
}
/// <summary>
/// 車を 1 固定 tick に 1 回進める(<see cref="VehicleMotor"/>)。物理の同期の後、物理の tick の前に動き、この tick の力の命令を積む。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(StaticMeshColliderSync))]
[RunsBefore(typeof(PhysicsStep))]
internal struct VehicleMotionUpdate:ISystem {
    private static readonly ConditionalWeakTable<DeterministicPhysicsWorld,VehicleMotor>Motors=[];
    public void Update(Entity Entity,ref Transform Transform,ref Vehicle Vehicle,ref VehicleState Car,ref VehicleIntent Intent,IPhysicsHost Host,ref State State){
        if(Transform.Frame!=Host.Physics.Frame||State.FixedTickRate<=0){
            return;
        }
        var Motor=Motors.GetValue(Host.Physics.Active.MutableWorld,static Key=>new VehicleMotor(Key));
        try {
            Motor.Step(Vehicle,ref Car,ref Intent,Entity,State.FixedTick,LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate));
        }
        catch(OverflowException){
            // 数値が収まらない状態では、その tick は力を加えない(物理世界は自分の溢れを Fault にする)。
        }
    }
}
/// <summary>
/// 物理が書き戻した車体の向き(<c>PhysicsRotation</c>)を Transform にも書く。子の車輪・カメラ・ライトが車体と一緒に回る。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsTransformWriteback))]
internal struct VehicleBodyWriteback:ISystem {
    public void Update(ref Transform Transform,ref Vehicle Vehicle,ref PhysicsRotation Rotation){
        Transform.Rotation=Rotation.Value;
    }
}
/// <summary>
/// 車輪の見た目(UE の ABP の WheelController)。親の車の車輪の状態から、休みの位置からサスペンションの上下だけ動かし、舵角で上の軸のまわりに、
/// 回転の角で右の軸のまわりに回す(前進で前の端が下がる向き)。
/// </summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct VehicleWheelPose:ISystem {
    public void Update(ref Transform Transform,ref VehicleWheelVisual Visual,in Parent<VehicleState> Car){
        if(!Visual.Captured){
            Visual.RestPosition=Transform.Position;
            Visual.RestRotation=Transform.Rotation;
            Visual.Captured=true;
        }
        if(Visual.Wheel<0||Visual.Wheel>3||!VehicleWheelVisual.TryPose(Visual,Car.Value.WheelAt(Visual.Wheel),out var Position,out var Rotation)){
            return;
        }
        Transform.Position=Position;
        Transform.Rotation=Rotation;
    }
}
/// <summary>
/// 追従カメラの向き(UE の BackSpringArm: Yaw だけを車から継ぎ、見回しの Yaw を足す。Pitch は継がない)。見回しの角度は毎フレーム FInterpTo で 0 へ戻る
/// (UE の InterpsToOriginalRotation)。腕の遅れ(UE の CameraRotationLagSpeed)は <c>OrbitCamera.RotationLag</c> が行う。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(OrbitCameraUpdate))]
internal struct VehicleChaseView:ISystem {
    public void Update(in Transform Transform,ref ViewAngles View,ref VehicleCameraLook Look,ref State State){
        Look.YawDegrees=InterpTo(Look.YawDegrees,0f,State.FrameDeltaSeconds,Look.ReturnSpeed);
        VehicleMotor.Basis(Transform.Rotation,out var Forward,out _,out _);
        if(!Forward.R.TryToSingle(out var R)||!Forward.F.TryToSingle(out var F)||(R==0f&&F==0f)){
            return;
        }
        View.YawDegrees=MathF.Atan2(R,F)*(180f/MathF.PI)+Look.YawDegrees;
        View.PitchDegrees=Look.PitchDegrees;
    }
    /// <summary>UE の FMath::FInterpTo: 残りの差 × clamp(Δt × 速さ, 0, 1) だけ近づく。速さが 0 以下ならすぐ着く。</summary>
    internal static float InterpTo(float Current,float Target,float DeltaSeconds,float Speed){
        if(Speed<=0f){ return Target; }
        var Distance=Target-Current;
        if(Distance*Distance<1e-8f){ return Target; }
        return Current+(Distance*Math.Clamp(DeltaSeconds*Speed,0f,1f));
    }
}
