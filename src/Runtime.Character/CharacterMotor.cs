using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// 決定的な物理ワールドに対して<see cref="LogicalScalar"/>で実行する、キネマティックなキャラクター移動の固定ティック1回分（ADR-0067）。
/// 貫通解消による押し戻しで衝突後に滑らせる。サブステップごとの移動をカプセル半径の半分以下に制限し、最も深い接触から押し出して、
/// そこへ向かう速度成分を除去する。最大<see cref="ResolveIterations"/>回繰り返す。歩行中は、歩行可能面との接触ならカプセルを真上に持ち上げて速度を変えず
///（坂道でも水平速度を維持。Unrealと同じ）、それ以外の接触は壁として水平成分だけを押し出す。床は自身の法線が歩行可能な面なので、段差の端にも立てる。
/// 歩行時はStepHeight以内の床へ吸着し、ブロックされた移動はStepHeightだけ高い位置から再試行する。
/// ワールドへの変更は行わない。
/// </summary>
public sealed class CharacterMotor {
    public const int ResolveIterations=4;
    private const int MaximumSubSteps=16;
    private const int FloorBisections=10;
    private static readonly LogicalScalar Gravity=LogicalScalar.FromSingle(9.80665f);
    private static readonly LogicalScalar Skin=LogicalScalar.FromSingle(0.001f);
    private static readonly LogicalScalar FloorProbeDistance=LogicalScalar.FromSingle(0.02f);
    private static readonly LogicalScalar SpeedEpsilon=LogicalScalar.FromSingle(0.001f);
    private static readonly LogicalScalar DegreesToRadians=LogicalMath.Pi/LogicalScalar.FromInt64(180);
    private static readonly LogicalScalar Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
    private readonly List<PhysicsShapeContact>Contacts=[];
    private readonly DeterministicPhysicsWorld World;
    private CharacterMotion Settings;
    private Collider Capsule;
    private LogicalScalar WalkableCos;
    public CharacterMotor(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary><paramref name="DeltaSeconds"/>分、<paramref name="Position"/>（カプセル中心）と<paramref name="State"/>を進め、ジャンプ要求を消費する。</summary>
    public void Step(in CharacterMotion Settings,ref LogicalVector3 Position,ref CharacterMotionState State,ref MoveIntent Input,LogicalScalar DeltaSeconds){
        this.Configure(Settings);
        if(Settings.ConstrainToPlane&&Settings.SnapToPlaneAtStart&&!State.PlaneSnapped){
            Position=this.ConstrainLocation(Position);
        }
        State.PlaneSnapped=true;
        var Move=this.ConstrainDirection(new LogicalVector3(Input.Direction.R,LogicalScalar.Zero,Input.Direction.F));
        var InputAmount=Length(Move);
        if(InputAmount>LogicalScalar.One){
            Move/=InputAmount;
            InputAmount=LogicalScalar.One;
        }
        var Direction=InputAmount.IsZero?LogicalVector3.Zero:Move/InputAmount;
        if(Input.JumpRequested&&State.Mode==CharacterMotionMode.Walking){
            State.Velocity=new LogicalVector3(State.Velocity.R,Settings.JumpSpeed,State.Velocity.F);
            State.Mode=CharacterMotionMode.Falling;
        }
        Input.JumpRequested=false;
        this.UpdateVelocity(ref State,Direction,InputAmount,DeltaSeconds);
        State.Velocity=this.ConstrainDirection(State.Velocity);
        this.Move(ref Position,ref State,DeltaSeconds);
        if(Settings.FaceMovement&&!InputAmount.IsZero){
            State.Facing=this.RotateToward(State.Facing,Direction,Settings.TurnRateDegrees*DegreesToRadians*DeltaSeconds);
        }
    }
    /// <summary>+Fを<paramref name="Facing"/>（水平な単位ベクトル）へ向けるYaw回転。向きがゼロなら恒等回転。</summary>
    public static LogicalQuaternion FacingRotation(LogicalVector3 Facing){
        if(Facing.LengthSquared.IsZero){
            return LogicalQuaternion.Identity;
        }
        // cos(theta) = F、sin(theta) = R。逆三角関数を使わず、半角の公式で求める。
        var Cos=Facing.F;
        LogicalScalar.TrySquareRoot(Clamp((LogicalScalar.One+Cos)*Half,LogicalScalar.Zero,LogicalScalar.One),out var HalfCos);
        LogicalScalar.TrySquareRoot(Clamp((LogicalScalar.One-Cos)*Half,LogicalScalar.Zero,LogicalScalar.One),out var HalfSin);
        if(Facing.R<LogicalScalar.Zero){
            HalfSin=-HalfSin;
        }
        return new LogicalQuaternion(LogicalScalar.Zero,HalfSin,LogicalScalar.Zero,HalfCos);
    }
    /// <summary>回転の水平向き（+F軸を水平面に投影したもの）。真上または真下を向く回転では+Fを返す。</summary>
    public static LogicalVector3 FacingOf(LogicalQuaternion Rotation){
        if(!Rotation.TryRotate(LogicalVector3.UnitF,out var Forward)){
            return LogicalVector3.UnitF;
        }
        return new LogicalVector3(Forward.R,LogicalScalar.Zero,Forward.F).TryNormalize(out var Facing)?Facing:LogicalVector3.UnitF;
    }
    private void Configure(in CharacterMotion Settings){
        this.Settings=Settings;
        var Radius=Settings.CapsuleRadius;
        var SegmentHalf=Settings.CapsuleHalfHeight-Radius;
        this.Capsule=new Collider { Shape=ColliderShape.Capsule,Radius=Radius,HalfHeight=SegmentHalf<LogicalScalar.Zero?LogicalScalar.Zero:SegmentHalf };
        LogicalMath.TrySinCos(Settings.MaxSlopeDegrees*DegreesToRadians,out _,out this.WalkableCos);
    }
    private void UpdateVelocity(ref CharacterMotionState State,LogicalVector3 Direction,LogicalScalar InputAmount,LogicalScalar DeltaSeconds){
        var Settings=this.Settings;
        var Horizontal=new LogicalVector3(State.Velocity.R,LogicalScalar.Zero,State.Velocity.F);
        var Vertical=State.Velocity.U;
        if(State.Mode==CharacterMotionMode.Walking){
            var MaxSpeed=Max(Settings.WalkSpeed*InputAmount,Settings.MinAnalogWalkSpeed);
            Horizontal=CalculateVelocity(Horizontal,Direction,Settings.Acceleration*InputAmount,Settings.GroundFriction,Settings.BrakingFriction,Settings.Braking,MaxSpeed,DeltaSeconds);
            Vertical=LogicalScalar.Zero;
        }
        else {
            var AirControl=Settings.AirControl;
            if(Horizontal.LengthSquared<Settings.AirControlBoostVelocityThreshold*Settings.AirControlBoostVelocityThreshold){
                AirControl*=Settings.AirControlBoostMultiplier;
            }
            Horizontal=CalculateVelocity(Horizontal,Direction,Settings.Acceleration*InputAmount*AirControl,LogicalScalar.Zero,LogicalScalar.Zero,Settings.AirBraking,Settings.WalkSpeed,DeltaSeconds);
            Vertical-=Gravity*Settings.GravityScale*DeltaSeconds;
        }
        State.Velocity=new LogicalVector3(Horizontal.R,Vertical,Horizontal.F);
    }
    /// <summary>水平面に対するUnrealのCalcVelocity。</summary>
    private static LogicalVector3 CalculateVelocity(LogicalVector3 Velocity,LogicalVector3 Direction,LogicalScalar Acceleration,LogicalScalar Friction,LogicalScalar BrakingFriction,LogicalScalar BrakingDeceleration,LogicalScalar MaxSpeed,LogicalScalar DeltaSeconds){
        if(Acceleration.IsZero){
            var Speed=Length(Velocity);
            if(Speed<=SpeedEpsilon){
                return LogicalVector3.Zero;
            }
            var Braked=Velocity+(Velocity*(-BrakingFriction)-Velocity/Speed*BrakingDeceleration)*DeltaSeconds;
            return LogicalVector3.Dot(Braked,Velocity)<=LogicalScalar.Zero?LogicalVector3.Zero:Braked;
        }
        var CurrentSpeed=Length(Velocity);
        var Turn=Min(DeltaSeconds*Friction,LogicalScalar.One);
        Velocity-=(Velocity-Direction*CurrentSpeed)*Turn;
        Velocity+=Direction*(Acceleration*DeltaSeconds);
        var NewSpeed=Length(Velocity);
        return NewSpeed>MaxSpeed?Velocity/NewSpeed*MaxSpeed:Velocity;
    }
    private void Move(ref LogicalVector3 Position,ref CharacterMotionState State,LogicalScalar DeltaSeconds){
        var Origin=Position;
        var Displacement=this.ConstrainDirection(State.Velocity*DeltaSeconds);
        var MaxSubStep=this.Settings.CapsuleRadius*Half;
        var Distance=Length(Displacement);
        var SubSteps=1;
        while(SubSteps<MaximumSubSteps&&Distance>MaxSubStep*LogicalScalar.FromInt64(SubSteps)){
            SubSteps++;
        }
        var SubDisplacement=Displacement/LogicalScalar.FromInt64(SubSteps);
        for(var Index=0;Index<SubSteps;Index++){
            this.MoveSubStep(ref Position,ref State,SubDisplacement);
        }
        if(State.Mode==CharacterMotionMode.Walking){
            if(this.TryFindFloor(Position,this.Settings.StepHeight+FloorProbeDistance,out var FloorHeight,out _)){
                Position=new LogicalVector3(Position.R,FloorHeight,Position.F);
            }
            else {
                State.Mode=CharacterMotionMode.Falling;
            }
        }
        // 貫通解消と床への吸着では法線方向にも押し出されることがある。UEのSafeMoveUpdatedComponentと同様、移動全体を平面へ投影する。
        Position=Origin+this.ConstrainDirection(Position-Origin);
    }
    /// <summary>UEのConstrainDirectionToPlane。<paramref name="Direction"/>から平面法線方向の成分を取り除く。制約がない場合、または法線がゼロの場合は変更しない。</summary>
    private LogicalVector3 ConstrainDirection(LogicalVector3 Direction){
        if(!this.Settings.ConstrainToPlane||!this.Settings.PlaneNormal.TryNormalize(out var Normal)){
            return Direction;
        }
        return Direction-(Normal*LogicalVector3.Dot(Direction,Normal));
    }
    /// <summary>UEのConstrainLocationToPlane。原点を通る平面へ点を投影する（SnapToPlaneAtStartで一度だけ使用）。制約がない場合、または法線がゼロの場合は変更しない。</summary>
    private LogicalVector3 ConstrainLocation(LogicalVector3 Point){
        if(!this.Settings.ConstrainToPlane||!this.Settings.PlaneNormal.TryNormalize(out var Normal)){
            return Point;
        }
        return Point-(Normal*LogicalVector3.Dot(Point-this.Settings.PlaneOrigin,Normal));
    }
    private void MoveSubStep(ref LogicalVector3 Position,ref CharacterMotionState State,LogicalVector3 Displacement){
        var Start=Position;
        var StartVelocity=State.Velocity;
        Position+=Displacement;
        var Blocked=this.Resolve(ref Position,ref State);
        if(!Blocked||State.Mode!=CharacterMotionMode.Walking||this.Settings.StepHeight<=LogicalScalar.Zero){
            return;
        }
        // 壁によって歩行移動が止められた場合、StepHeightだけ上げて再試行する。次の条件を満たせばその移動を採用する。
        // 歩行可能な床に着地し、その接触点が足元からStepHeight以内にある（Unrealの規則）。
        // かつ、ブロックされた元の移動より遠くまで進める。
        var Raised=Start+LogicalVector3.UnitU*this.Settings.StepHeight;
        if(this.Collect(Raised)>0){
            return;
        }
        var Stepped=Raised+new LogicalVector3(Displacement.R,LogicalScalar.Zero,Displacement.F);
        var SteppedState=State with { Velocity=StartVelocity };
        if(this.Resolve(ref Stepped,ref SteppedState)&&HorizontalDistanceSquared(Stepped,Start)<=HorizontalDistanceSquared(Position,Start)){
            return;
        }
        var Feet=Start.U-this.Settings.CapsuleHalfHeight;
        if(!this.TryFindFloor(Stepped,this.Settings.StepHeight+FloorProbeDistance,out var FloorHeight,out var FloorPoint)
            ||FloorPoint-Feet>this.Settings.StepHeight+Skin
            ||HorizontalDistanceSquared(Stepped,Start)<=HorizontalDistanceSquared(Position,Start)){
            return;
        }
        Position=new LogicalVector3(Stepped.R,FloorHeight,Stepped.F);
        State.Velocity=SteppedState.Velocity;
    }
    /// <summary>最も深い接触から最大4回押し出す。歩行中に壁（歩行不可の接触）が移動を妨げた場合はtrue。</summary>
    private bool Resolve(ref LogicalVector3 Position,ref CharacterMotionState State){
        var Blocked=false;
        for(var Iteration=0;Iteration<ResolveIterations;Iteration++){
            if(this.Collect(Position)==0){
                break;
            }
            var Deepest=this.Contacts[0];
            foreach(var Contact in this.Contacts){
                if(Contact.Penetration>Deepest.Penetration){
                    Deepest=Contact;
                }
            }
            var Outward=-Deepest.Normal;
            if(State.Mode==CharacterMotionMode.Walking&&Outward.U>=this.WalkableCos){
                Position+=LogicalVector3.UnitU*(Deepest.Penetration/Outward.U+Skin);
                continue;
            }
            var Push=Outward;
            var PushDistance=Deepest.Penetration;
            if(State.Mode==CharacterMotionMode.Walking){
                var Flat=new LogicalVector3(Outward.R,LogicalScalar.Zero,Outward.F);
                var FlatLength=Length(Flat);
                if(FlatLength>SpeedEpsilon){
                    Push=Flat/FlatLength;
                    PushDistance=Deepest.Penetration/FlatLength;
                }
                Blocked=true;
            }
            Position+=Push*(PushDistance+Skin);
            var Into=LogicalVector3.Dot(State.Velocity,Push);
            if(Into<LogicalScalar.Zero){
                State.Velocity-=Push*Into;
            }
            if(State.Mode==CharacterMotionMode.Falling&&this.Supports(Deepest)&&State.Velocity.U<=LogicalScalar.Zero){
                State.Mode=CharacterMotionMode.Walking;
                State.Velocity=new LogicalVector3(State.Velocity.R,LogicalScalar.Zero,State.Velocity.F);
            }
        }
        return Blocked;
    }
    /// <summary>カプセルを上向きに押し出す、歩行可能面との接触。床面またはその端。</summary>
    private bool Supports(in PhysicsShapeContact Contact)=>Contact.SurfaceNormal.U>=this.WalkableCos&&-Contact.Normal.U>SpeedEpsilon;
    /// <summary>
    /// <paramref name="Position"/>より<paramref name="Distance"/>以内の下方にある、最も高い歩行可能な床面でカプセル中心が静止する高さ。
    /// Unrealの下向き床スイープと同様に、床を支える接触が見つかるまでカプセルを半径の半分ずつ下降させ、その後、最初に接触する高さを二分探索する。
    /// 壁は無視するため、段差の端も上から検出できる（端に立つ）。カプセルが既に床へめり込んでいる場合は、真上へ持ち上げて抜け出す。
    /// </summary>
    private bool TryFindFloor(LogicalVector3 Position,LogicalScalar Distance,out LogicalScalar Height,out LogicalScalar FloorPoint){
        Height=Position.U;
        FloorPoint=Position.U;
        if(this.HasSupport(Position,out FloorPoint)){
            var Lifted=Position;
            for(var Iteration=0;Iteration<ResolveIterations+2;Iteration++){
                this.Collect(Lifted);
                var Rise=LogicalScalar.Zero;
                foreach(var Contact in this.Contacts){
                    if(this.Supports(Contact)){
                        var Candidate=Contact.Penetration/(-Contact.Normal.U);
                        if(Candidate>Rise){
                            Rise=Candidate;
                        }
                    }
                }
                if(Rise.IsZero){
                    break;
                }
                Lifted+=LogicalVector3.UnitU*Rise;
            }
            Height=Lifted.U;
            return true;
        }
        var StepSize=this.Settings.CapsuleRadius*Half;
        var Free=Position.U;
        var Bottom=Position.U-Distance;
        var Touching=Free;
        var Hit=false;
        while(Free>Bottom){
            var Sample=Max(Free-StepSize,Bottom);
            if(this.HasSupport(new LogicalVector3(Position.R,Sample,Position.F),out var SamplePoint)){
                FloorPoint=SamplePoint;
                Touching=Sample;
                Hit=true;
                break;
            }
            Free=Sample;
        }
        if(!Hit){
            return false;
        }
        for(var Iteration=0;Iteration<FloorBisections;Iteration++){
            var Middle=(Free+Touching)*Half;
            if(this.HasSupport(new LogicalVector3(Position.R,Middle,Position.F),out var MiddlePoint)){
                FloorPoint=MiddlePoint;
                Touching=Middle;
            }
            else {
                Free=Middle;
            }
        }
        Height=Free;
        return true;
    }
    /// <summary>支持接触がある場合true。<paramref name="PointHeight"/>は最も高い支持接触点の高さ。</summary>
    private bool HasSupport(LogicalVector3 Position,out LogicalScalar PointHeight){
        this.Collect(Position);
        var Found=false;
        PointHeight=LogicalScalar.Zero;
        foreach(var Contact in this.Contacts){
            if(this.Supports(Contact)&&(!Found||Contact.Point.U>PointHeight)){
                PointHeight=Contact.Point.U;
                Found=true;
            }
        }
        return Found;
    }
    private int Collect(LogicalVector3 Position){
        this.Contacts.Clear();
        this.World.CollectContacts(this.Capsule,Position,LogicalQuaternion.Identity,this.Settings.CollidesWith,this.Contacts);
        return this.Contacts.Count;
    }
    private LogicalVector3 RotateToward(LogicalVector3 Facing,LogicalVector3 Target,LogicalScalar MaxRadians){
        if(Facing.LengthSquared.IsZero){
            return Target;
        }
        var Cos=LogicalVector3.Dot(Facing,Target);
        LogicalMath.TrySinCos(MaxRadians,out var StepSin,out var StepCos);
        if(Cos>=StepCos){
            return Target;
        }
        var Toward=Target-Facing*Cos;
        if(!Toward.TryNormalize(out var Perpendicular)){
            // 特異な場合を避けるため、+Uまわりの反対方向へ回転する（右手系）。
            Perpendicular=new LogicalVector3(Facing.F,LogicalScalar.Zero,-Facing.R);
        }
        var Rotated=Facing*StepCos+Perpendicular*StepSin;
        return Rotated.TryNormalize(out var Result)?Result:Target;
    }
    private static LogicalScalar HorizontalDistanceSquared(LogicalVector3 Left,LogicalVector3 Right){
        var R=Left.R-Right.R;
        var F=Left.F-Right.F;
        return R*R+F*F;
    }
    private static LogicalScalar Length(LogicalVector3 Value)=>LogicalScalar.TrySquareRoot(Value.LengthSquared,out var Result)?Result:LogicalScalar.Zero;
    private static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
    private static LogicalScalar Clamp(LogicalScalar Value,LogicalScalar Low,LogicalScalar High)=>Value<Low?Low:Value>High?High:Value;
}
