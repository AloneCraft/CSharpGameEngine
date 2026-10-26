using System.Text;
using System.Text.Json;
using CSharpGameEngine.Core;
using static CSharpGameEngine.Unreal.SceneImporter.SceneSourceFormatting;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// BP_ThirdPersonCharacterのCDO実値とEnhanced Inputのマッピングから、テンプレートのキャラクターSceneと
/// 入力マップのC#を生成する(ADR-0067)。UEのcmはmへ、UEの(X前,Y右,Z上)はエンジンの(R,U,F)=(Y,Z,X)へ
/// 換算する。移植先に無い機能(カメララグ、ソケット/ターゲットオフセット、落下中の横摩擦等)が
/// 有効な値で来た場合は、黙って捨てずに例外にする。
/// </summary>
internal static class ThirdPersonGameplayEmitter {
    /// <summary>入力Context/ActionのStableIdを名前から決めるための名前空間(このテンプレート固有の固定値)。</summary>
    /// <summary>テンプレートのウィンドウ(1280x720)の縦横比。UEのFOVは水平角、Cameraは垂直角で持つ。</summary>
    private const float TemplateAspect=1280f/720f;
    internal static string BuildCharacterSource(ThirdPersonCharacterManifest Character,ImportPlayerStart Player){
        var Movement=Character.Movement;
        if(Character.CapsuleRadiusCm<=0f||Character.CapsuleHalfHeightCm<=0f||Movement.MaxWalkSpeedCm<=0f){
            throw new InvalidOperationException("The ThirdPerson manifest has no character capsule/movement values; re-run the Unreal export.");
        }
        if(Movement.FallingLateralFriction!=0f){
            throw new NotSupportedException($"FallingLateralFriction {Movement.FallingLateralFriction} is not supported by CharacterMotion.");
        }
        var Arm=Character.OrbitCamera;
        if(!Arm.UsePawnControlRotation||Arm.EnableCameraLag||Arm.SocketOffsetCm.Any(Value=>Value!=0f)||Arm.TargetOffsetCm.Any(Value=>Value!=0f)){
            throw new NotSupportedException("OrbitCamera supports only bUsePawnControlRotation without camera lag, socket offset or target offset.");
        }
        if(Character.Camera.UsePawnControlRotation||Character.Camera.FieldOfViewDegrees<=0f||Character.Camera.FieldOfViewDegrees>=180f){
            throw new NotSupportedException("The follow camera must use the arm's rotation and a field of view in (0, 180) degrees.");
        }
        // Unreal は bUseSeparateBrakingFriction が true の場合にのみ BrakingFriction を適用する。それ以外では GroundFriction を使い、どちらも BrakingFrictionFactor を掛ける。
        var BrakingFriction=(Movement.UseSeparateBrakingFriction?Movement.BrakingFriction:Movement.GroundFriction)*Movement.BrakingFrictionFactor;
        var FovY=2f*MathF.Atan(MathF.Tan(Character.Camera.FieldOfViewDegrees*MathF.PI/360f)/TemplateAspect);
        var Pivot=Character.OrbitCamera.RelativeLocationCm;
        var Builder=new StringBuilder();
        Builder.AppendLine("using CSharpGameEngine.Entities;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Animation;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Character;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Windows;");
        Builder.AppendLine("namespace "+ThirdPersonTemplateImporter.Namespace+";");
        Builder.AppendLine("/// <summary>");
        Builder.AppendLine("/// UEの"+Character.Blueprint+"の移植。PlayerStartの位置に置く。値はBlueprintのCDO実値をmへ換算したもの。");
        Builder.AppendLine("/// 見た目はQuinnとABP_Unarmed相当のロコモーション(ADR-0066)。カメラの水平FOV "+Character.Camera.FieldOfViewDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)+"°は16:9での垂直FOVへ換算している。");
        Builder.AppendLine("/// </summary>");
        Builder.AppendLine("[StartupScene(Order=2)]");
        Builder.AppendLine("internal partial class ThirdPersonCharacter:IScene {");
        Builder.AppendLine("    public Node Root=>new(){");
        Builder.AppendLine("        Name=\"BP_ThirdPersonCharacter\",");
        Builder.AppendLine("        Components=[");
        Builder.Append("            new Transform { Position=new LogicalVector3(").Append(F(Player.Translation[0])).Append(',').Append(F(Player.Translation[1])).Append(',').Append(F(Player.Translation[2]))
            .Append("),Rotation=new LogicalQuaternion(new LogicalVector3(0f,").Append(F(FacingYawRadians(Player))).AppendLine(",0f)) },");
        Builder.AppendLine("            new CharacterMotion {");
        Builder.Append("                CapsuleRadius=").Append(L(Character.CapsuleRadiusCm/100f)).Append(",CapsuleHalfHeight=").Append(L(Character.CapsuleHalfHeightCm/100f)).AppendLine(",");
        Builder.Append("                WalkSpeed=").Append(L(Movement.MaxWalkSpeedCm/100f)).Append(",MinAnalogWalkSpeed=").Append(L(Movement.MinAnalogWalkSpeedCm/100f)).Append(",Acceleration=").Append(L(Movement.MaxAccelerationCm/100f)).AppendLine(",");
        Builder.Append("                Braking=").Append(L(Movement.BrakingDecelerationWalkingCm/100f)).Append(",AirBraking=").Append(L(Movement.BrakingDecelerationFallingCm/100f))
            .Append(",GroundFriction=").Append(L(Movement.GroundFriction)).Append(",BrakingFriction=").Append(L(BrakingFriction)).AppendLine(",");
        Builder.Append("                JumpSpeed=").Append(L(Movement.JumpZVelocityCm/100f)).Append(",AirControl=").Append(L(Movement.AirControl))
            .Append(",AirControlBoostMultiplier=").Append(L(Movement.AirControlBoostMultiplier)).Append(",AirControlBoostVelocityThreshold=").Append(L(Movement.AirControlBoostVelocityThresholdCm/100f)).AppendLine(",");
        Builder.Append("                GravityScale=").Append(L(Movement.GravityScale)).Append(",StepHeight=").Append(L(Movement.MaxStepHeightCm/100f)).Append(",MaxSlopeDegrees=").Append(L(Movement.MaxSlopeDegrees)).AppendLine(",");
        Builder.Append("                TurnRateDegrees=").Append(L(Movement.RotationRateYawDegrees)).Append(",FaceMovement=").Append(Movement.FaceMovement?"true":"false").AppendLine(",");
        Builder.AppendLine("            },");
        Builder.AppendLine("            new CharacterMotionState(),");
        Builder.AppendLine("            new MoveIntent(),");
        Builder.Append("            new ViewAngles { YawDegrees=").Append(F(FacingYawRadians(Player)*180f/MathF.PI)).AppendLine(" },");
        Builder.AppendLine("        ],");
        Builder.AppendLine("        Children=[");
        var Mesh=Character.Mesh;
        if(Mesh.RelativeRotation.Pitch!=0f||Mesh.RelativeRotation.Roll!=0f){
            throw new NotSupportedException("The character mesh may only be yawed relative to the capsule.");
        }
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"Mesh\",");
        Builder.AppendLine("                Components=[");
        // Unreal のアクター座標系 (X 前方、Y 右、Z 上) は、キャラクターの (+F, +R, +U) 座標系であり、
        // キャラクターは Unreal アクターの +X と同じ向きである +F を向く。Unreal の相対 yaw ψ は、エンジンの yaw ψ - 90° となる。
        Builder.Append("                    new Transform { Position=new LogicalVector3(").Append(F(Mesh.RelativeLocationCm[1]/100f)).Append(',').Append(F(Mesh.RelativeLocationCm[2]/100f)).Append(',').Append(F(Mesh.RelativeLocationCm[0]/100f))
            .Append("),Rotation=new LogicalQuaternion(new LogicalVector3(0f,").Append(F(MeshYawRadians(Mesh.RelativeRotation.Yaw))).AppendLine(",0f)) },");
        Builder.Append("                    new SkeletalMesh { AssetPath=ThirdPersonAssets.").Append(AssetMemberName(Mesh.AssetPath)).AppendLine(" },");
        var (WalkSpeed,JogSpeed)=ForwardSampleSpeeds(Character);
        Builder.AppendLine("                    new Locomotion {");
        Builder.Append("                        Idle=ThirdPersonAssets.").Append(ClipMember(Character,"Idle")).Append(",Walk=ThirdPersonAssets.").Append(ClipMember(Character,"WalkForward")).Append(",Jog=ThirdPersonAssets.").Append(ClipMember(Character,"JogForward")).AppendLine(",");
        Builder.Append("                        Jump=ThirdPersonAssets.").Append(ClipMember(Character,"Jump")).Append(",Fall=ThirdPersonAssets.").Append(ClipMember(Character,"FallLoop")).Append(",Land=ThirdPersonAssets.").Append(ClipMember(Character,"Land")).AppendLine(",");
        Builder.Append("                        WalkSpeed=").Append(F(WalkSpeed)).Append(",JogSpeed=").Append(F(JogSpeed)).AppendLine(",");
        Builder.AppendLine("                    },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"FollowCamera\",");
        Builder.AppendLine("                Components=[");
        Builder.AppendLine("                    new Transform(),");
        Builder.Append("                    new OrbitCamera { Pivot=new LogicalVector3(").Append(F(Pivot[1]/100f)).Append(',').Append(F(Pivot[2]/100f)).Append(',').Append(F(Pivot[0]/100f))
            .Append("),Distance=").Append(L(Arm.TargetArmLengthCm/100f)).Append(",Radius=").Append(L(Arm.ProbeSizeCm/100f)).Append(",Collide=").Append(Arm.Collide?"true":"false").AppendLine(" },");
        Builder.Append("                    new Camera { Active=true,FovYRadians=").Append(F(FovY)).AppendLine(",FarZ=1000f },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        Builder.AppendLine("        ],");
        Builder.AppendLine("    };");
        Builder.AppendLine("}");
        return Builder.ToString();
    }
    /// <summary>LogicalScalar フィールドには、Scene DSL が許可する暗黙変換を利用して float リテラルを設定する。</summary>
    private static string L(float Value)=>F(Value);
    /// <summary>度数で表した Unreal 相対 yaw に対する、キャラクター基準のメッシュのエンジン yaw。</summary>
    internal static float MeshYawRadians(float UnrealYawDegrees){
        var Degrees=(UnrealYawDegrees-90f)%360f;
        Degrees=Degrees>180f?Degrees-360f:Degrees<=-180f?Degrees+360f:Degrees;
        return Degrees*MathF.PI/180f;
    }
    private static string ClipMember(ThirdPersonCharacterManifest Character,string Name)=>
        AssetMemberName(Character.Animations.SingleOrDefault(Clip=>Clip.Name==Name)?.AssetPath??throw new InvalidOperationException($"The manifest has no '{Name}' animation."));
    /// <summary>
    /// BS_Idle_Walk_Run の前進列（方向 0）における歩行・ジョギング速度 (m/s)。移動方向を向くキャラクターが使う列はこれだけ。
    /// </summary>
    internal static (float Walk,float Jog)ForwardSampleSpeeds(ThirdPersonCharacterManifest Character){
        float Speed(string Clip){
            var Name=Path.GetFileNameWithoutExtension(Character.Animations.Single(Item=>Item.Name==Clip).AssetPath);
            var Sample=Character.BlendSpace.Samples.SingleOrDefault(Item=>Item.X==0f&&Item.Animation.EndsWith("."+Name,StringComparison.Ordinal))
                ??throw new InvalidOperationException($"BS_Idle_Walk_Run has no forward sample for {Name}.");
            return Sample.Y/100f;
        }
        return (Speed("WalkForward"),Speed("JogForward"));
    }
    /// <summary>
    /// Unreal アクターの前方を向くためのエンジン yaw（+F を +R の方向へ回転）。レベル規約では Unreal (X, Y, Z) をエンジン (R, U, -F) に対応付ける。Unreal の前方 +X はエンジン +R となり、Unreal の yaw ψ はエンジンの yaw ψ と同じ向きに回転する。マニフェストの yawRadians は -ψ であるため、向きの yaw は ψ + 90° = 90° - yawRadians となる。
    /// </summary>
    internal static float FacingYawRadians(ImportPlayerStart Player)=>MathF.PI/2f-Player.YawRadians;
}
internal sealed class ThirdPersonCharacterManifest {
    public string Blueprint { get; set; }=string.Empty;
    public float CapsuleRadiusCm { get; set; }
    public float CapsuleHalfHeightCm { get; set; }
    public ThirdPersonMovementManifest Movement { get; set; }=new();
    public ThirdPersonMeshManifest Mesh { get; set; }=new();
    public ThirdPersonBlendSpaceManifest BlendSpace { get; set; }=new();
    public ThirdPersonOrbitCameraManifest OrbitCamera { get; set; }=new();
    public ThirdPersonCameraManifest Camera { get; set; }=new();
    public List<ThirdPersonAnimationManifest>Animations { get; set; }=[];
}
internal sealed class ThirdPersonBlendSpaceManifest {
    public List<ThirdPersonBlendSampleManifest>Samples { get; set; }=[];
}
internal sealed class ThirdPersonBlendSampleManifest {
    public string Animation { get; set; }=string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
}
internal sealed class ThirdPersonRotatorManifest {
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float Roll { get; set; }
}
internal sealed class ThirdPersonMovementManifest {
    public bool FaceMovement { get; set; }
    public float RotationRateYawDegrees { get; set; }
    public float JumpZVelocityCm { get; set; }
    public float AirControl { get; set; }
    public float AirControlBoostMultiplier { get; set; }
    public float AirControlBoostVelocityThresholdCm { get; set; }
    public float MaxWalkSpeedCm { get; set; }
    public float MinAnalogWalkSpeedCm { get; set; }
    public float MaxAccelerationCm { get; set; }
    public float BrakingDecelerationWalkingCm { get; set; }
    public float BrakingDecelerationFallingCm { get; set; }
    public float GroundFriction { get; set; }
    public float BrakingFrictionFactor { get; set; }
    public float BrakingFriction { get; set; }
    public bool UseSeparateBrakingFriction { get; set; }
    public float FallingLateralFriction { get; set; }
    public float GravityScale { get; set; }
    public float MaxStepHeightCm { get; set; }
    public float MaxSlopeDegrees { get; set; }
}
internal sealed class ThirdPersonOrbitCameraManifest {
    public float[]RelativeLocationCm { get; set; }=[0f,0f,0f];
    public float TargetArmLengthCm { get; set; }
    public bool UsePawnControlRotation { get; set; }
    public bool Collide { get; set; }
    public float ProbeSizeCm { get; set; }
    public float[]SocketOffsetCm { get; set; }=[0f,0f,0f];
    public float[]TargetOffsetCm { get; set; }=[0f,0f,0f];
    public bool EnableCameraLag { get; set; }
}
internal sealed class ThirdPersonCameraManifest {
    public float FieldOfViewDegrees { get; set; }
    public bool UsePawnControlRotation { get; set; }
}
