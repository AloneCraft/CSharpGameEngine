using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// One fixed tick of kinematic character movement (ADR-0067) against the deterministic physics
/// world, in <see cref="LogicalScalar"/>. Collide-and-slide by depenetration: each sub-step moves at
/// most half the capsule radius, then pushes out of the deepest contact and removes the velocity
/// into it, up to <see cref="ResolveIterations"/> times. While walking, a walkable contact lifts the
/// capsule straight up without touching the velocity (horizontal speed is kept on ramps, as in
/// Unreal) and any other contact is a wall (horizontal push only). Floors are faces whose own normal
/// is walkable, so the capsule can perch on a step's edge; walking snaps to floors within
/// MaxStepHeight and retries blocked moves raised by MaxStepHeight. The world is only read.
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
    private CharacterMovement Settings;
    private Collider Capsule;
    private LogicalScalar WalkableCos;
    public CharacterMotor(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary>Advances <paramref name="Position"/> (the capsule centre) and <paramref name="State"/> by <paramref name="DeltaSeconds"/>; consumes the jump request.</summary>
    public void Step(in CharacterMovement Settings,ref LogicalVector3 Position,ref CharacterMovementState State,ref CharacterInput Input,LogicalScalar DeltaSeconds){
        this.Configure(Settings);
        var Move=new LogicalVector3(Input.Move.R,LogicalScalar.Zero,Input.Move.F);
        var InputAmount=Length(Move);
        if(InputAmount>LogicalScalar.One){
            Move/=InputAmount;
            InputAmount=LogicalScalar.One;
        }
        var Direction=InputAmount.IsZero?LogicalVector3.Zero:Move/InputAmount;
        if(Input.Jump&&State.Mode==CharacterMovementMode.Walking){
            State.Velocity=new LogicalVector3(State.Velocity.R,Settings.JumpZVelocity,State.Velocity.F);
            State.Mode=CharacterMovementMode.Falling;
        }
        Input.Jump=false;
        this.UpdateVelocity(ref State,Direction,InputAmount,DeltaSeconds);
        this.Move(ref Position,ref State,DeltaSeconds);
        if(Settings.OrientRotationToMovement&&!InputAmount.IsZero){
            State.Facing=this.RotateToward(State.Facing,Direction,Settings.RotationRateDegrees*DegreesToRadians*DeltaSeconds);
        }
    }
    /// <summary>Yaw rotation turning +F into <paramref name="Facing"/> (horizontal unit vector); identity for a zero facing.</summary>
    public static LogicalQuaternion FacingRotation(LogicalVector3 Facing){
        if(Facing.LengthSquared.IsZero){
            return LogicalQuaternion.Identity;
        }
        // cos(theta) = F, sin(theta) = R; half-angle identities avoid an inverse trigonometric function.
        var Cos=Facing.F;
        LogicalScalar.TrySquareRoot(Clamp((LogicalScalar.One+Cos)*Half,LogicalScalar.Zero,LogicalScalar.One),out var HalfCos);
        LogicalScalar.TrySquareRoot(Clamp((LogicalScalar.One-Cos)*Half,LogicalScalar.Zero,LogicalScalar.One),out var HalfSin);
        if(Facing.R<LogicalScalar.Zero){
            HalfSin=-HalfSin;
        }
        return new LogicalQuaternion(LogicalScalar.Zero,HalfSin,LogicalScalar.Zero,HalfCos);
    }
    /// <summary>Horizontal facing of a rotation (its +F axis flattened); +F when the rotation looks straight up or down.</summary>
    public static LogicalVector3 FacingOf(LogicalQuaternion Rotation){
        if(!Rotation.TryRotate(LogicalVector3.UnitF,out var Forward)){
            return LogicalVector3.UnitF;
        }
        return new LogicalVector3(Forward.R,LogicalScalar.Zero,Forward.F).TryNormalize(out var Facing)?Facing:LogicalVector3.UnitF;
    }
    private void Configure(in CharacterMovement Settings){
        this.Settings=Settings;
        var Radius=Settings.CapsuleRadius;
        var SegmentHalf=Settings.CapsuleHalfHeight-Radius;
        this.Capsule=new Collider { Shape=ColliderShape.Capsule,Radius=Radius,HalfHeight=SegmentHalf<LogicalScalar.Zero?LogicalScalar.Zero:SegmentHalf };
        LogicalMath.TrySinCos(Settings.WalkableFloorAngleDegrees*DegreesToRadians,out _,out this.WalkableCos);
    }
    private void UpdateVelocity(ref CharacterMovementState State,LogicalVector3 Direction,LogicalScalar InputAmount,LogicalScalar DeltaSeconds){
        var Settings=this.Settings;
        var Horizontal=new LogicalVector3(State.Velocity.R,LogicalScalar.Zero,State.Velocity.F);
        var Vertical=State.Velocity.U;
        if(State.Mode==CharacterMovementMode.Walking){
            var MaxSpeed=Max(Settings.MaxWalkSpeed*InputAmount,Settings.MinAnalogWalkSpeed);
            Horizontal=CalculateVelocity(Horizontal,Direction,Settings.MaxAcceleration*InputAmount,Settings.GroundFriction,Settings.BrakingFriction,Settings.BrakingDecelerationWalking,MaxSpeed,DeltaSeconds);
            Vertical=LogicalScalar.Zero;
        }
        else {
            var AirControl=Settings.AirControl;
            if(Horizontal.LengthSquared<Settings.AirControlBoostVelocityThreshold*Settings.AirControlBoostVelocityThreshold){
                AirControl*=Settings.AirControlBoostMultiplier;
            }
            Horizontal=CalculateVelocity(Horizontal,Direction,Settings.MaxAcceleration*InputAmount*AirControl,LogicalScalar.Zero,LogicalScalar.Zero,Settings.BrakingDecelerationFalling,Settings.MaxWalkSpeed,DeltaSeconds);
            Vertical-=Gravity*Settings.GravityScale*DeltaSeconds;
        }
        State.Velocity=new LogicalVector3(Horizontal.R,Vertical,Horizontal.F);
    }
    /// <summary>Unreal's CalcVelocity for the horizontal plane.</summary>
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
    private void Move(ref LogicalVector3 Position,ref CharacterMovementState State,LogicalScalar DeltaSeconds){
        var Displacement=State.Velocity*DeltaSeconds;
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
        if(State.Mode==CharacterMovementMode.Walking){
            if(this.TryFindFloor(Position,this.Settings.MaxStepHeight+FloorProbeDistance,out var FloorHeight,out _)){
                Position=new LogicalVector3(Position.R,FloorHeight,Position.F);
            }
            else {
                State.Mode=CharacterMovementMode.Falling;
            }
        }
    }
    private void MoveSubStep(ref LogicalVector3 Position,ref CharacterMovementState State,LogicalVector3 Displacement){
        var Start=Position;
        var StartVelocity=State.Velocity;
        Position+=Displacement;
        var Blocked=this.Resolve(ref Position,ref State);
        if(!Blocked||State.Mode!=CharacterMovementMode.Walking||this.Settings.MaxStepHeight<=LogicalScalar.Zero){
            return;
        }
        // A wall stopped a walking move: retry it raised by MaxStepHeight and keep it if it lands on a
        // walkable floor whose contact point is at most MaxStepHeight above the feet (Unreal's rule)
        // and gets further than the blocked move.
        var Raised=Start+LogicalVector3.UnitU*this.Settings.MaxStepHeight;
        if(this.Collect(Raised)>0){
            return;
        }
        var Stepped=Raised+new LogicalVector3(Displacement.R,LogicalScalar.Zero,Displacement.F);
        var SteppedState=State with { Velocity=StartVelocity };
        if(this.Resolve(ref Stepped,ref SteppedState)&&HorizontalDistanceSquared(Stepped,Start)<=HorizontalDistanceSquared(Position,Start)){
            return;
        }
        var Feet=Start.U-this.Settings.CapsuleHalfHeight;
        if(!this.TryFindFloor(Stepped,this.Settings.MaxStepHeight+FloorProbeDistance,out var FloorHeight,out var FloorPoint)
            ||FloorPoint-Feet>this.Settings.MaxStepHeight+Skin
            ||HorizontalDistanceSquared(Stepped,Start)<=HorizontalDistanceSquared(Position,Start)){
            return;
        }
        Position=new LogicalVector3(Stepped.R,FloorHeight,Stepped.F);
        State.Velocity=SteppedState.Velocity;
    }
    /// <summary>Pushes out of the deepest contact up to four times; true when a wall (non-walkable contact while walking) blocked the move.</summary>
    private bool Resolve(ref LogicalVector3 Position,ref CharacterMovementState State){
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
            if(State.Mode==CharacterMovementMode.Walking&&Outward.U>=this.WalkableCos){
                Position+=LogicalVector3.UnitU*(Deepest.Penetration/Outward.U+Skin);
                continue;
            }
            var Push=Outward;
            var PushDistance=Deepest.Penetration;
            if(State.Mode==CharacterMovementMode.Walking){
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
            if(State.Mode==CharacterMovementMode.Falling&&this.Supports(Deepest)&&State.Velocity.U<=LogicalScalar.Zero){
                State.Mode=CharacterMovementMode.Walking;
                State.Velocity=new LogicalVector3(State.Velocity.R,LogicalScalar.Zero,State.Velocity.F);
            }
        }
        return Blocked;
    }
    /// <summary>A contact on a walkable face that pushes the capsule upward: a floor, or the edge of one.</summary>
    private bool Supports(in PhysicsShapeContact Contact)=>Contact.SurfaceNormal.U>=this.WalkableCos&&-Contact.Normal.U>SpeedEpsilon;
    /// <summary>
    /// Height the capsule centre must have to rest on the highest walkable floor within
    /// <paramref name="Distance"/> below <paramref name="Position"/>, like Unreal's downward floor
    /// sweep: the capsule descends in steps of half its radius until a supporting contact appears,
    /// then bisects to the first touching height. Walls are ignored, so a step's edge is found from
    /// above (a perch). A capsule already in a floor is lifted straight out of it instead.
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
    /// <summary>True when a supporting contact exists; <paramref name="PointHeight"/> is the highest supporting contact point.</summary>
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
            // Exactly opposite: turn about +U (right-handed) to leave the singular case.
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
