using System.Runtime.CompilerServices;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// 物理ステップ後、静的ジオメトリと同期した状態で、固定ティックごとに各キャラクターを1回移動させる。
/// キャラクターはルートノードでなければならない。そのTransform位置が物理フレーム内のカプセル中心となる。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsTransformWriteback))]
internal struct CharacterMotionUpdate:ISystem {
    private static readonly ConditionalWeakTable<DeterministicPhysicsWorld,CharacterMotor>Motors=[];
    public void Update(Entity Entity,ref Transform Transform,ref CharacterMotion Movement,ref CharacterMotionState MovementState,ref MoveIntent Input,in EventWriter<Jumped> Jumps,in EventWriter<Landed> Lands,in EventWriter<Footstep> Steps,IPhysicsHost Host,ref State State){
        if(Transform.Frame!=Host.Physics.Frame){
            return;
        }
        var World=Host.Physics.Active.MutableWorld;
        var Motor=Motors.GetValue(World,static Key=>new CharacterMotor(Key));
        if(MovementState.Facing.LengthSquared.IsZero){
            MovementState.Facing=CharacterMotor.FacingOf(Transform.Rotation);
        }
        var Position=Transform.Position;
        Input.Steer(Movement,Position);
        var WasWalking=MovementState.Mode==CharacterMotionMode.Walking;
        var WasVerticalSpeed=MovementState.Velocity.U;
        var JumpWanted=Input.JumpRequested;
        var DeltaSeconds=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        try {
            Motor.Step(Movement,ref Position,ref MovementState,ref Input,DeltaSeconds);
        }
        catch(OverflowException){
            // 数値的に無効な設定の場合、このティックではキャラクターを元の位置に留める。
            return;
        }
        Transform.Position=Position;
        if(Movement.FaceMovement){
            Transform.Rotation=CharacterMotor.FacingRotation(MovementState.Facing);
        }
        // ADR-0077：このティックに起きたことを、サウンドやアニメーションの処理へ通知する。
        if(WasWalking&&JumpWanted&&MovementState.Mode==CharacterMotionMode.Falling){
            Jumps.Send(new Jumped { Source=Entity });
        }
        if(!WasWalking&&MovementState.Mode==CharacterMotionMode.Walking){
            var Impact=0f;
            if(WasVerticalSpeed.TryToSingle(out var Fall)){
                Impact=Math.Clamp(-Fall/8f,0f,1f);
            }
            Lands.Send(new Landed { Source=Entity,Impact=Impact });
            MovementState.StrideDistance=LogicalScalar.Zero;
        }
        if(MovementState.Mode==CharacterMotionMode.Walking&&Movement.StepLength>LogicalScalar.Zero){
            var Sideways=MovementState.Velocity.R;
            var Forward=MovementState.Velocity.F;
            if(LogicalScalar.TrySquareRoot(Sideways*Sideways+Forward*Forward,out var Speed)){
                MovementState.StrideDistance+=Speed*DeltaSeconds;
                while(MovementState.StrideDistance>=Movement.StepLength){
                    MovementState.StrideDistance-=Movement.StepLength;
                    Steps.Send(new Footstep { Source=Entity });
                }
            }
        } else {
            MovementState.StrideDistance=LogicalScalar.Zero;
        }
    }
}
/// <summary>
/// 固定ティック後、レンダラーがカメラ姿勢を取得する前（CameraOriginSync、order 1）にスプリングアームカメラを配置する。
/// アームのEntityは、<see cref="ViewAngles"/>を持つキャラクタールートの直接の子である必要がある。
/// ワールド姿勢がアーム先端での制御回転となるよう、アームのローカルTransformを書き換える。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
internal struct OrbitCameraUpdate:ISystem {
    private static readonly ConditionalWeakTable<DeterministicPhysicsWorld,OrbitCameraSolver>Solvers=[];
    public void Update(ref Transform Transform,ref OrbitCamera Arm,ref OrbitCameraRuntime Memory,in Parent<ViewAngles> Pawn,IPhysicsHost Host,ref State State){
        var Controller=Pawn.Value;
        if(!State.World.TryGetComponent(Pawn.Entity,out Transform Owner)||Owner.Frame!=Host.Physics.Frame){
            return;
        }
        var Solver=Solvers.GetValue(Host.Physics.Active.MutableWorld,static Key=>new OrbitCameraSolver(Key));
        var Control=PlayerControl.ControlRotation(Controller);
        var Inverse=Owner.Rotation.Conjugate();
        try {
            if(!Owner.Rotation.TryRotate(Arm.Pivot,out var PivotOffset)){
                return;
            }
            var Pivot=Owner.Position+PivotOffset;
            var DeltaSeconds=State.FrameDeltaSeconds;
            // ADR-0078/M0107：UEのカメララグ。最初のフレームでは遅延元がないため、カメラを本来の位置から開始する。
            if(Arm.Lag.TryToSingle(out var LagSpeed)&&LagSpeed>0f&&Memory.HasPrevious){
                Arm.LagMaxTimeStep.TryToSingle(out var MaxTimeStep);
                Arm.LagMaxDistance.TryToSingle(out var MaxDistance);
                Pivot=CameraLag.Pivot(Memory.Pivot,Pivot,DeltaSeconds,LagSpeed,MaxTimeStep,MaxDistance);
            }
            if(Arm.RotationLag.TryToSingle(out var RotationLagSpeed)&&RotationLagSpeed>0f&&Control.TryToSingle(out var Target)){
                if(Memory.HasPrevious){
                    Arm.LagMaxTimeStep.TryToSingle(out var MaxTimeStep);
                    Target=CameraLag.Rotation(Memory.Rotation,Target,DeltaSeconds,RotationLagSpeed,MaxTimeStep);
                    var Lagged=new LogicalQuaternion(LogicalScalar.FromSingle(Target.X),LogicalScalar.FromSingle(Target.Y),LogicalScalar.FromSingle(Target.Z),LogicalScalar.FromSingle(Target.W));
                    if(Lagged.TryNormalize(out var Normalized)){
                        Control=Normalized;
                    }
                }
                Memory.Rotation=Target;
            }
            Memory.Pivot=Pivot;
            Memory.HasPrevious=true;
            if(!Solver.TrySolveFrom(Arm,Pivot,Control,out var CameraPosition,out _)
                ||!Inverse.TryRotate(CameraPosition-Owner.Position,out var Local)
                ||!LogicalQuaternion.TryMultiply(Inverse,Control,out var LocalRotation)
                ||Owner.Scale.R.IsZero||Owner.Scale.U.IsZero||Owner.Scale.F.IsZero){
                return;
            }
            Transform.Position=new LogicalVector3(Local.R/Owner.Scale.R,Local.U/Owner.Scale.U,Local.F/Owner.Scale.F);
            Transform.Rotation=LocalRotation;
        }
        catch(OverflowException){
            // 配置を表現できない場合、カメラは前フレームの位置を保つ。
        }
    }
}
