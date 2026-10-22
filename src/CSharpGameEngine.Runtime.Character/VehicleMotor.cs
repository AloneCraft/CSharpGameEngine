using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// 車の1固定tick(ADR-0077)。決定的な物理世界を読むだけで、<see cref="LogicalScalar"/>で解く。
/// 前後の速度と横方向の速度を車体の向きで分けて、前後はアクセル・ブレーキ・自然な減速、横は<see cref="Vehicle.Grip"/>で消す。
/// ハンドルは前輪の角度から旋回の速さを求める(自転車モデル)。接地は4つの車輪の位置から車体の下へ飛ばした光線で調べ、
/// 当たった4点から地面の高さと傾きを求めて、車体をそこへ寄せる。接地している車輪が2つ未満なら、空中として重力で飛ぶ。
/// 進む向きの壁(急な面)には光線で当たり、前後の速度を失う。右はこのエンジンの座標系で、前×上の向き(+Rは左)。
/// </summary>
public sealed class VehicleMotor {
    private static readonly LogicalScalar Gravity=LogicalScalar.FromSingle(9.80665f);
    private static readonly LogicalScalar DegreesToRadians=LogicalMath.Pi/LogicalScalar.FromInt64(180);
    private static readonly LogicalScalar Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
    private static readonly LogicalScalar Quarter=LogicalScalar.One/LogicalScalar.FromInt64(4);
    /// <summary>車輪の光線の始点を、車体の中心からこれだけ上に置く(m)。車体が地面に少し沈んでも、光線が地面の下から始まらない。</summary>
    private static readonly LogicalScalar RayLift=LogicalScalar.FromSingle(0.5f);
    /// <summary>止まっているとみなす前後の速度(m/s)。これ未満で逆向きに踏むと、ブレーキでなく発進になる。</summary>
    private static readonly LogicalScalar StopSpeed=LogicalScalar.FromSingle(0.5f);
    private static readonly LogicalScalar HeightFollow=LogicalScalar.FromSingle(25f);
    private static readonly LogicalScalar AlignRate=LogicalScalar.FromSingle(12f);
    private static readonly LogicalScalar AirAlignRate=LogicalScalar.FromSingle(1.5f);
    /// <summary>これより急な面(法線の上向き成分がこれ未満)は壁とみなす。</summary>
    private static readonly LogicalScalar WallSlope=LogicalScalar.FromSingle(0.6f);
    /// <summary>前の車軸から、前の端(バンパー)までの距離(m)。壁の光線の始点。</summary>
    private static readonly LogicalScalar NoseReach=LogicalScalar.FromSingle(0.9f);
    /// <summary>壁の手前に残す隙間(m)。</summary>
    private static readonly LogicalScalar WallSkin=LogicalScalar.FromSingle(0.05f);
    /// <summary>壁の光線を、このtickの前進の距離よりさらに先まで伸ばす長さ(m)。</summary>
    private static readonly LogicalScalar WallLookAhead=LogicalScalar.FromSingle(0.1f);
    private static readonly LogicalScalar[]Fronts=[Half,-Half];
    private static readonly LogicalScalar[]Sides=[-Half,Half];
    private readonly List<PhysicsRayHit>Hits=[];
    private readonly DeterministicPhysicsWorld World;
    public VehicleMotor(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary>車体の中心<paramref name="Position"/>・向き<paramref name="Rotation"/>・<paramref name="State"/>を、<paramref name="DeltaSeconds"/>だけ進める。</summary>
    public void Step(in Vehicle Settings,ref LogicalVector3 Position,ref LogicalQuaternion Rotation,ref VehicleState State,in VehicleIntent Intent,LogicalScalar DeltaSeconds){
        var Drive=ClampUnit(Intent.Drive);
        var Handbrake=Intent.Handbrake;
        State.Steering=Approach(State.Steering,ClampUnit(Intent.Steering),Settings.SteeringRate*DeltaSeconds);
        Basis(Rotation,out var Forward,out var Right,out var Up);
        var Ground=this.Probe(Settings,Position,Forward,Right,Up,out var Average,out var Normal);
        if(Ground){
            var Velocity=State.Velocity;
            var Speed=LogicalVector3.Dot(Velocity,Forward);
            var Lateral=LogicalVector3.Dot(Velocity,Right);
            Speed=this.Longitudinal(Settings,Speed,Drive,Handbrake,DeltaSeconds);
            var Grip=Handbrake?Settings.HandbrakeGrip:Settings.Grip;
            Lateral-=Lateral*Min(LogicalScalar.One,Grip*DeltaSeconds);
            if(this.TryFindWall(Settings,Position,Forward,Right,Speed,DeltaSeconds,out var Gap)){
                // 壁までの隙間で、このtickの前進を打ち切る(壁に着いたら前後の速度は失う)。壁には入り込まない。
                var Allowed=Max(LogicalScalar.Zero,Gap-WallSkin);
                if(Abs(Speed)*DeltaSeconds>Allowed){
                    Speed=Sign(Speed)*Allowed/DeltaSeconds;
                }
            }
            State.Velocity=Forward*Speed+Right*Lateral;
            if(this.TryYaw(Settings,Speed,State.Steering,DeltaSeconds,out var Yaw)&&LogicalQuaternion.TryMultiply(Rotation,Yaw,out var Turned)){
                Rotation=Turned;
            }
            Position+=State.Velocity*DeltaSeconds;
        }
        else {
            State.Velocity=new LogicalVector3(State.Velocity.R,State.Velocity.U-Gravity*Settings.GravityScale*DeltaSeconds,State.Velocity.F);
            Position+=State.Velocity*DeltaSeconds;
            Align(ref Rotation,Up,LogicalVector3.UnitU,AirAlignRate*DeltaSeconds);
        }
        Basis(Rotation,out Forward,out Right,out Up);
        State.OnGround=this.Probe(Settings,Position,Forward,Right,Up,out Average,out Normal);
        if(State.OnGround){
            // 光線の4点の平均から法線の向きにRideHeight離れた所が、車体の中心の高さ。下がるときはなだらかに、めり込むときはすぐに直す。
            var Target=Average+Normal*Settings.RideHeight;
            var Follow=Target.U>Position.U?LogicalScalar.One:Min(LogicalScalar.One,HeightFollow*DeltaSeconds);
            Position=new LogicalVector3(Position.R,Position.U+(Target.U-Position.U)*Follow,Position.F);
            Align(ref Rotation,Up,Normal,AlignRate*DeltaSeconds);
            Basis(Rotation,out Forward,out Right,out Up);
            if(!Ground){
                // 着地: 地面の向きの速度は残し、地面へ入る速度だけを消す。
                var Into=LogicalVector3.Dot(State.Velocity,Normal);
                if(Into<LogicalScalar.Zero){
                    State.Velocity-=Normal*Into;
                }
            }
            var Along=LogicalVector3.Dot(State.Velocity,Forward);
            var Across=LogicalVector3.Dot(State.Velocity,Right);
            State.Velocity=Forward*Along+Right*Across;
            State.ForwardSpeed=Along;
        }
        else {
            State.ForwardSpeed=LogicalVector3.Dot(State.Velocity,Forward);
        }
        State.Gear=GearOf(Settings,State.ForwardSpeed);
    }
    /// <summary>車体の前・右(前×上)・上の向き。</summary>
    public static void Basis(LogicalQuaternion Rotation,out LogicalVector3 Forward,out LogicalVector3 Right,out LogicalVector3 Up){
        if(!Rotation.TryRotate(LogicalVector3.UnitF,out Forward)||!Rotation.TryRotate(LogicalVector3.UnitU,out Up)){
            Forward=LogicalVector3.UnitF;
            Up=LogicalVector3.UnitU;
        }
        Right=LogicalVector3.Cross(Forward,Up);
    }
    /// <summary>速度の絶対値から決める表示用のギア。後退は-1、ほぼ停止は0、前進は1から<see cref="Vehicle.GearCount"/>。</summary>
    public static int GearOf(in Vehicle Settings,LogicalScalar ForwardSpeed){
        if(Settings.GearCount<=0||(ForwardSpeed<StopSpeed&&ForwardSpeed>-StopSpeed)){
            return 0;
        }
        if(ForwardSpeed<LogicalScalar.Zero){
            return -1;
        }
        if(Settings.TopSpeed<=LogicalScalar.Zero){
            return 1;
        }
        var Fraction=ForwardSpeed/Settings.TopSpeed*LogicalScalar.FromInt64(Settings.GearCount);
        var Gear=1;
        while(Gear<Settings.GearCount&&Fraction>=LogicalScalar.FromInt64(Gear)){
            Gear++;
        }
        return Gear;
    }
    private LogicalScalar Longitudinal(in Vehicle Settings,LogicalScalar Speed,LogicalScalar Drive,bool Handbrake,LogicalScalar DeltaSeconds){
        var Acceleration=LogicalScalar.Zero;
        if(Drive>LogicalScalar.Zero){
            if(Speed<-StopSpeed){
                Acceleration=Settings.BrakeDeceleration*Drive;
            }
            else {
                Acceleration=Settings.Acceleration*Drive*Max(LogicalScalar.Zero,LogicalScalar.One-Max(Speed,LogicalScalar.Zero)/Settings.TopSpeed);
            }
        }
        else if(Drive<LogicalScalar.Zero){
            if(Speed>StopSpeed){
                Acceleration=Settings.BrakeDeceleration*Drive;
            }
            else {
                Acceleration=Settings.Acceleration*Drive*Max(LogicalScalar.Zero,LogicalScalar.One-Max(-Speed,LogicalScalar.Zero)/Settings.ReverseSpeed);
            }
        }
        else {
            Acceleration=-Sign(Speed)*Min(Settings.RollingDeceleration,Abs(Speed)/DeltaSeconds);
        }
        if(Handbrake){
            Acceleration-=Sign(Speed)*Min(Settings.BrakeDeceleration*Half,Abs(Speed)/DeltaSeconds);
        }
        var Next=Speed+Acceleration*DeltaSeconds;
        if((Speed>LogicalScalar.Zero&&Next<LogicalScalar.Zero&&Drive<=LogicalScalar.Zero)||(Speed<LogicalScalar.Zero&&Next>LogicalScalar.Zero&&Drive>=LogicalScalar.Zero)){
            // ブレーキや自然な減速は、止まるところで終わる(そのまま逆向きには走り出さない)。
            Next=LogicalScalar.Zero;
        }
        return Next;
    }
    /// <summary>前輪の角度と前後の速度から求めた、このtickの旋回(上向きの軸のまわり)。右に切ると、上向きの軸のまわりでは負の向き。</summary>
    private bool TryYaw(in Vehicle Settings,LogicalScalar Speed,LogicalScalar Steering,LogicalScalar DeltaSeconds,out LogicalQuaternion Yaw){
        Yaw=LogicalQuaternion.Identity;
        if(Settings.Wheelbase<=LogicalScalar.Zero){
            return false;
        }
        var Angle=Steering*Settings.MaxSteeringDegrees*DegreesToRadians/(LogicalScalar.One+Abs(Speed)/Max(Settings.SteeringFalloffSpeed,LogicalScalar.One));
        if(!LogicalMath.TrySinCos(Angle,out var Sin,out var Cos)||Cos.IsZero){
            return false;
        }
        var YawRate=Speed*(Sin/Cos)/Settings.Wheelbase;
        if(Settings.MaxLateralAcceleration>LogicalScalar.Zero){
            // タイヤが出せる横方向の加速度の限界: |速度×角速度|がこれを超えないように、角速度を抑える。
            var Limit=Settings.MaxLateralAcceleration/Max(Abs(Speed),LogicalScalar.One);
            YawRate=YawRate>Limit?Limit:YawRate<-Limit?-Limit:YawRate;
        }
        return LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,-YawRate*DeltaSeconds,out Yaw);
    }
    /// <summary>
    /// 進む向きの車体の前(後退なら後ろ)の2隅から、水平な光線を飛ばして、急な面(壁)までの隙間(m)を求める。
    /// 光線の長さはこのtickの前進の距離に余裕を足したもので、それより遠い壁は見ない。緩やかな面(坂)は壁ではない。
    /// </summary>
    private bool TryFindWall(in Vehicle Settings,LogicalVector3 Position,LogicalVector3 Forward,LogicalVector3 Right,LogicalScalar Speed,LogicalScalar DeltaSeconds,out LogicalScalar Gap){
        Gap=default;
        if(Speed.IsZero){
            return false;
        }
        var Heading=new LogicalVector3(Forward.R,LogicalScalar.Zero,Forward.F);
        if(!Heading.TryNormalize(out Heading)){
            return false;
        }
        var Direction=Speed<LogicalScalar.Zero?-Heading:Heading;
        var Reach=Settings.Wheelbase*Half+NoseReach;
        var Distance=Abs(Speed)*DeltaSeconds+WallLookAhead;
        var Found=false;
        foreach(var Side in Sides){
            var Origin=Position+Direction*Reach+Right*(Settings.Track*Side);
            this.Hits.Clear();
            this.World.Raycast(new PhysicsRay(Origin,Direction,Distance),Settings.CollidesWith,this.Hits);
            foreach(var Hit in this.Hits){
                if(Abs(Hit.Normal.U)<WallSlope&&(!Found||Hit.Distance<Gap)){
                    Found=true;
                    Gap=Hit.Distance;
                }
            }
        }
        return Found;
    }
    /// <summary>
    /// 4つの車輪の位置から車体の下へ光線を飛ばす。2つ以上が地面に当たれば接地で、<paramref name="Average"/>は4点(外れた車輪は伸びきった位置)の平均、
    /// <paramref name="Normal"/>は4点が作る面の法線(上向き)。
    /// </summary>
    private bool Probe(in Vehicle Settings,LogicalVector3 Position,LogicalVector3 Forward,LogicalVector3 Right,LogicalVector3 Up,out LogicalVector3 Average,out LogicalVector3 Normal){
        Average=Position;
        Normal=Up;
        var Length=RayLift+Settings.RideHeight+Settings.SuspensionTravel;
        Span<LogicalVector3>Points=stackalloc LogicalVector3[4];
        var Touching=0;
        var Index=0;
        foreach(var Front in Fronts){
            foreach(var Side in Sides){
                var Origin=Position+Up*RayLift+Forward*(Settings.Wheelbase*Front)+Right*(Settings.Track*Side);
                this.Hits.Clear();
                this.World.Raycast(new PhysicsRay(Origin,-Up,Length),Settings.CollidesWith,this.Hits);
                var Found=false;
                var Nearest=LogicalScalar.Zero;
                var Point=Origin-Up*Length;
                foreach(var Hit in this.Hits){
                    if(Hit.Normal.U>=WallSlope&&(!Found||Hit.Distance<Nearest)){
                        Found=true;
                        Nearest=Hit.Distance;
                        Point=Hit.Point;
                    }
                }
                if(Found){
                    Touching++;
                }
                Points[Index++]=Point;
            }
        }
        if(Touching<2){
            return false;
        }
        Average=(Points[0]+Points[1]+Points[2]+Points[3])*Quarter;
        // 前の2点(0,1)と後ろの2点(2,3)。左右は-Right側、+Right側の順。
        var Across=(Points[1]+Points[3])-(Points[0]+Points[2]);
        var Along=(Points[0]+Points[1])-(Points[2]+Points[3]);
        if(LogicalVector3.Cross(Across,Along).TryNormalize(out var Candidate)&&Candidate.U>LogicalScalar.Zero){
            Normal=Candidate;
        }
        return true;
    }
    /// <summary>車体の上<paramref name="Up"/>を、<paramref name="Target"/>へ、角度の<paramref name="Blend"/>(0から1)の割合だけ回す。小さい角度の近似。</summary>
    private static void Align(ref LogicalQuaternion Rotation,LogicalVector3 Up,LogicalVector3 Target,LogicalScalar Blend){
        var Axis=LogicalVector3.Cross(Up,Target);
        if(Axis.LengthSquared.IsZero){
            return;
        }
        var K=Min(LogicalScalar.One,Blend)*Half;
        var Delta=new LogicalQuaternion(Axis.R*K,Axis.U*K,Axis.F*K,LogicalScalar.One);
        if(Delta.TryNormalize(out Delta)&&LogicalQuaternion.TryMultiply(Delta,Rotation,out var Aligned)&&Aligned.TryNormalize(out Aligned)){
            Rotation=Aligned;
        }
    }
    private static LogicalScalar ClampUnit(float Value)=>float.IsFinite(Value)?LogicalScalar.FromSingle(Math.Clamp(Value,-1f,1f)):LogicalScalar.Zero;
    private static LogicalScalar ClampUnit(LogicalScalar Value)=>Value<-LogicalScalar.One?-LogicalScalar.One:Value>LogicalScalar.One?LogicalScalar.One:Value;
    private static LogicalScalar Approach(LogicalScalar Value,LogicalScalar Target,LogicalScalar MaxStep)=>
        Target>Value?Min(Target,Value+MaxStep):Max(Target,Value-MaxStep);
    private static LogicalScalar Sign(LogicalScalar Value)=>Value>LogicalScalar.Zero?LogicalScalar.One:Value<LogicalScalar.Zero?-LogicalScalar.One:LogicalScalar.Zero;
    private static LogicalScalar Abs(LogicalScalar Value)=>Value<LogicalScalar.Zero?-Value:Value;
    private static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
}
