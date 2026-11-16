using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>ゲームプレイ入力コード用の制御回転ヘルパー（UnrealのAddControllerYaw/PitchInputおよびDoMove）。</summary>
public static class PlayerControl {
    /// <summary>
    /// UnrealのAddControllerYaw/PitchInputと同様、度数で視点入力を加える。正のYawは画面右へ回転する。
    /// <see cref="ViewAngles.YawDegrees"/>は+Fを+Rへ向けるが、右手系ビュー（Matrix4x4.CreateLookAt）では画面左に見えるため、
    /// Yaw入力の符号を反転する。Yawは[-180, 180)の範囲へ折り返す。Pitchはコントローラーの上下限がどちらの順でも、その間に収める。
    /// </summary>
    public static void AddLook(ref ViewAngles Controller,float YawDegrees,float PitchDegrees){
        if(float.IsFinite(YawDegrees)){
            var Yaw=(Controller.YawDegrees-YawDegrees)%360f;
            Controller.YawDegrees=Yaw>=180f?Yaw-360f:Yaw<-180f?Yaw+360f:Yaw;
        }
        if(float.IsFinite(PitchDegrees)){
            // Math.Clampは最小値が最大値を超えると例外をスローする。シーンで上下限が逆に指定されていても、範囲の指定として扱う。
            var Low=MathF.Min(Controller.MinPitchDegrees,Controller.MaxPitchDegrees);
            var High=MathF.Max(Controller.MinPitchDegrees,Controller.MaxPitchDegrees);
            Controller.PitchDegrees=Math.Clamp(Controller.PitchDegrees+PitchDegrees,Low,High);
        }
    }
    /// <summary>
    /// <see cref="ControlRotation"/>だけから取り出したYaw。<see cref="ViewAngles.YawDegrees"/>分、+U軸まわりに回転する。
    /// UEのbUseControllerRotationYawがPawnへ与える挙動であり、ボディは視点に合わせて旋回するが、上／下は向かない（ADR-0078/M0107）。
    /// </summary>
    public static LogicalQuaternion YawRotation(in ViewAngles Controller){
        var HalfYaw=Controller.YawDegrees*MathF.PI/360f;
        var Yaw=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Sin(HalfYaw)),LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfYaw)));
        return Yaw.TryNormalize(out var Result)?Result:LogicalQuaternion.Identity;
    }
    /// <summary>+Fを制御方向へ向けるワールド回転。+RまわりのPitchを適用した後、+UまわりのYawを適用する。</summary>
    public static LogicalQuaternion ControlRotation(in ViewAngles Controller){
        var HalfYaw=Controller.YawDegrees*MathF.PI/360f;
        var HalfPitch=Controller.PitchDegrees*MathF.PI/360f;
        // +Rまわりの正回転では+Fが-U方向を向くため、上を見る角度は負になる。
        var Yaw=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Sin(HalfYaw)),LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfYaw)));
        var Pitch=new LogicalQuaternion(LogicalScalar.FromSingle(-MathF.Sin(HalfPitch)),LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.FromSingle(MathF.Cos(HalfPitch)));
        return LogicalQuaternion.TryMultiply(Yaw,Pitch,out var Product)&&Product.TryNormalize(out var Result)?Result:LogicalQuaternion.Identity;
    }
    /// <summary>
    /// 制御Yawを基準にした2D移動入力（Xが右、Yが前方）に対応する、ワールド空間の水平移動方向。
    /// 長さは1を上限として制限し、<see cref="MoveIntent.Direction"/>に使う。
    /// </summary>
    public static LogicalVector3 MoveDirection(in ViewAngles Controller,float Right,float Forward){
        if(!float.IsFinite(Right)||!float.IsFinite(Forward)){
            return LogicalVector3.Zero;
        }
        var Length=MathF.Sqrt(Right*Right+Forward*Forward);
        if(Length>1f){
            Right/=Length;
            Forward/=Length;
        }
        var Yaw=Controller.YawDegrees*MathF.PI/180f;
        var Sin=MathF.Sin(Yaw);
        var Cos=MathF.Cos(Yaw);
        // Yawによって+Fは+Rを向く。したがって前方は(sin, 0, cos)。右手系ビューで画面右に向く方向は
        // Forward x Up = (-cos, 0, sin)。
        return new LogicalVector3(LogicalScalar.FromSingle(Forward*Sin-Right*Cos),LogicalScalar.Zero,LogicalScalar.FromSingle(Forward*Cos+Right*Sin));
    }
}
/// <summary>
/// 決定的な物理ワールドを使ったスプリングアーム配置。アームは、ピボットから後方へスイープするプローブ球付きカプセルを表す。
/// 何にも接触しない最長距離を二分探索で求める。カプセルは半径を大きくすると単調に重なるため、薄い壁を見落とさない。
/// </summary>
public sealed class OrbitCameraSolver {
    public const int Bisections=16;
    private static readonly LogicalScalar Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
    private static readonly LogicalScalar HalfSqrtTwo=LogicalScalar.FromSingle(0.70710678f);
    private readonly List<PhysicsShapeContact>Contacts=[];
    private readonly DeterministicPhysicsWorld World;
    public OrbitCameraSolver(DeterministicPhysicsWorld World){
        this.World=World??throw new ArgumentNullException(nameof(World));
    }
    /// <summary><paramref name="OwnerPosition"/>に所有者がいる場合のカメラのワールド位置。数値を表現できない場合はfalse。</summary>
    public bool TrySolve(in OrbitCamera Arm,in LogicalVector3 OwnerPosition,in LogicalQuaternion OwnerRotation,in LogicalQuaternion ControlRotation,out LogicalVector3 CameraPosition,out LogicalScalar ArmLength){
        CameraPosition=default;
        ArmLength=default;
        if(!OwnerRotation.TryRotate(Arm.Pivot,out var Offset)){
            return false;
        }
        return this.TrySolveFrom(Arm,OwnerPosition+Offset,ControlRotation,out CameraPosition,out ArmLength);
    }
    /// <summary>ピボットが既に<paramref name="Pivot"/>にあるアーム（一例としてラグ後のピボット）のカメラワールド位置。数値を表現できない場合はfalse。</summary>
    public bool TrySolveFrom(in OrbitCamera Arm,in LogicalVector3 Pivot,in LogicalQuaternion ControlRotation,out LogicalVector3 CameraPosition,out LogicalScalar ArmLength){
        CameraPosition=default;
        ArmLength=default;
        if(!ControlRotation.TryRotate(LogicalVector3.UnitF,out var Forward)){
            return false;
        }
        var Length=Arm.Distance>LogicalScalar.Zero?Arm.Distance:LogicalScalar.Zero;
        if(Arm.Collide&&Arm.Radius>LogicalScalar.Zero){
            // アームに沿ったカプセル軸（ローカル+U）。制御回転後に+Rまわりへ4分の1回転する。
            if(!LogicalQuaternion.TryMultiply(ControlRotation,new LogicalQuaternion(HalfSqrtTwo,LogicalScalar.Zero,LogicalScalar.Zero,HalfSqrtTwo),out var AxisRotation)){
                return false;
            }
            if(this.Blocked(Arm,Pivot,Forward,AxisRotation,Length)){
                var Clear=LogicalScalar.Zero;
                var Hit=Length;
                for(var Iteration=0;Iteration<Bisections;Iteration++){
                    var Middle=(Clear+Hit)*Half;
                    if(this.Blocked(Arm,Pivot,Forward,AxisRotation,Middle)){
                        Hit=Middle;
                    }
                    else {
                        Clear=Middle;
                    }
                }
                Length=Clear;
            }
        }
        ArmLength=Length;
        CameraPosition=Pivot-Forward*Length;
        if(Arm.SocketOffset!=LogicalVector3.Zero){
            if(!ControlRotation.TryRotate(Arm.SocketOffset,out var Socket)){
                return false;
            }
            CameraPosition+=Socket;
        }
        return true;
    }
    private bool Blocked(in OrbitCamera Arm,LogicalVector3 Pivot,LogicalVector3 Forward,LogicalQuaternion AxisRotation,LogicalScalar Length){
        var HalfLength=Length*Half;
        var Probe=new Collider { Shape=ColliderShape.Capsule,Radius=Arm.Radius,HalfHeight=HalfLength };
        this.Contacts.Clear();
        var Result=this.World.CollectContacts(Probe,Pivot-Forward*HalfLength,AxisRotation,Arm.CollidesWith,this.Contacts);
        // クエリを評価できない場合はブロック中と見なし、カメラをピボット側に留める。
        return Result.Status!=PhysicsQueryStatus.Success||this.Contacts.Count>0;
    }
}
/// <summary>
/// UEのSpringArmComponentのラグ処理（CameraLagSpeed、CameraLagMaxTimeStep、CameraLagMaxDistance、CameraRotationLagSpeed。ADR-0078/M0107）。
/// ピボットとビュー回転は指数的に目標へ近づく。最大時間ステップより長いフレームはその長さごとに分割するため、結果はフレームレートに依存しない。
/// プレゼンテーション専用であり、カメラをシミュレーションへ反映しない。
/// </summary>
public static class CameraLag {
    /// <summary>UEのKINDA_SMALL_NUMBER（1e-4 cm）をメートル単位で二乗した値。距離がこれより近ければ目標値を採用する。</summary>
    private static readonly LogicalScalar SmallDistanceSquared=LogicalScalar.FromSingle(1e-12f);
    /// <summary>
    /// UEのVInterpTo。距離に<c>clamp(dt * speed, 0, 1)</c>を掛けた分だけ<paramref name="Current"/>を
    /// <paramref name="Target"/>へ近づける。speedが0または距離がごく小さければ目標値を返す。
    /// </summary>
    public static LogicalVector3 InterpolateTo(LogicalVector3 Current,LogicalVector3 Target,float DeltaSeconds,float Speed){
        if(!(Speed>0f)||!float.IsFinite(Speed)){
            return Target;
        }
        var Distance=Target-Current;
        if(Distance.LengthSquared<SmallDistanceSquared){
            return Target;
        }
        var Fraction=Math.Clamp((float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f)*Speed,0f,1f);
        return Current+(Distance*LogicalScalar.FromSingle(Fraction));
    }
    /// <summary>
    /// ラグのあるピボット。<paramref name="Previous"/>を、毎秒<paramref name="Speed"/>の速度で<paramref name="Target"/>へ近づける。
    /// <paramref name="MaxTimeStep"/>より長いフレームは分割し、各ステップで時間に応じて目標位置も進める（UEのサブステップ処理）。
    /// <paramref name="MaxDistance"/>が0より大きければ、結果が目標からその距離を超えて離れないようにする。
    /// </summary>
    public static LogicalVector3 Pivot(LogicalVector3 Previous,LogicalVector3 Target,float DeltaSeconds,float Speed,float MaxTimeStep,float MaxDistance){
        if(!(Speed>0f)||!float.IsFinite(Speed)||!(DeltaSeconds>0f)||!float.IsFinite(DeltaSeconds)){
            return Target;
        }
        LogicalVector3 Result;
        if(MaxTimeStep>0f&&DeltaSeconds>MaxTimeStep){
            var Chase=Previous;
            var Step=(Target-Previous)*LogicalScalar.FromSingle(MaxTimeStep/DeltaSeconds);
            var Remaining=DeltaSeconds;
            Result=Previous;
            while(Remaining>1e-4f){
                var Amount=MathF.Min(MaxTimeStep,Remaining);
                Chase+=Step*LogicalScalar.FromSingle(Amount/MaxTimeStep);
                Remaining-=Amount;
                Result=InterpolateTo(Result,Chase,Amount,Speed);
            }
        }
        else {
            Result=InterpolateTo(Previous,Target,DeltaSeconds,Speed);
        }
        if(MaxDistance>0f&&LogicalScalar.TryFromSingle(MaxDistance,out var Limit)){
            var Behind=Result-Target;
            if(Behind.LengthSquared>Limit*Limit&&Behind.TryNormalize(out var Direction)){
                Result=Target+(Direction*Limit);
            }
        }
        return Result;
    }
    /// <summary>
    /// UEのQInterpTo。<c>clamp(dt * speed, 0, 1)</c>分だけ<paramref name="Current"/>から<paramref name="Target"/>へ
    /// slerpする。speedが0なら目標値を返す。長いフレームは<paramref name="MaxTimeStep"/>単位に分割する。
    /// </summary>
    public static System.Numerics.Quaternion Rotation(System.Numerics.Quaternion Current,System.Numerics.Quaternion Target,float DeltaSeconds,float Speed,float MaxTimeStep){
        if(!(Speed>0f)||!float.IsFinite(Speed)||!(DeltaSeconds>0f)||!float.IsFinite(DeltaSeconds)){
            return Target;
        }
        var Remaining=DeltaSeconds;
        var Result=Current;
        do {
            var Amount=MaxTimeStep>0f?MathF.Min(MaxTimeStep,Remaining):Remaining;
            Result=System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Slerp(Result,Target,Math.Clamp(Amount*Speed,0f,1f)));
            Remaining-=Amount;
        } while(Remaining>1e-4f);
        return Result;
    }
}
