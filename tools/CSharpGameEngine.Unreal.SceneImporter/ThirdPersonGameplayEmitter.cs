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
    internal const string InputNamespace="CgeThirdPerson.Input";
    /// <summary>入力Context/ActionのStableIdを名前から決めるための名前空間(このテンプレート固有の固定値)。</summary>
    private static readonly StableId InputIdNamespace=StableId.Parse("7c1b8a52-3f4e-5d9a-8e21-6b0c4f7d9a13");
    /// <summary>テンプレートのウィンドウ(1280x720)の縦横比。UEのFOVは水平角、Cameraは垂直角で持つ。</summary>
    private const float TemplateAspect=1280f/720f;
    internal static string BuildCharacterSource(ThirdPersonCharacterManifest Character,ImportPlayerStart Player){
        var Movement=Character.Movement;
        if(Character.CapsuleRadiusCm<=0f||Character.CapsuleHalfHeightCm<=0f||Movement.MaxWalkSpeedCm<=0f){
            throw new InvalidOperationException("The ThirdPerson manifest has no character capsule/movement values; re-run the Unreal export.");
        }
        if(Movement.FallingLateralFriction!=0f){
            throw new NotSupportedException($"FallingLateralFriction {Movement.FallingLateralFriction} is not supported by CharacterMovement.");
        }
        var Arm=Character.SpringArm;
        if(!Arm.UsePawnControlRotation||Arm.EnableCameraLag||Arm.SocketOffsetCm.Any(Value=>Value!=0f)||Arm.TargetOffsetCm.Any(Value=>Value!=0f)){
            throw new NotSupportedException("SpringArm supports only bUsePawnControlRotation without camera lag, socket offset or target offset.");
        }
        if(Character.Camera.UsePawnControlRotation||Character.Camera.FieldOfViewDegrees<=0f||Character.Camera.FieldOfViewDegrees>=180f){
            throw new NotSupportedException("The follow camera must use the arm's rotation and a field of view in (0, 180) degrees.");
        }
        // Unreal applies BrakingFriction only with bUseSeparateBrakingFriction; otherwise braking uses GroundFriction, both scaled by BrakingFrictionFactor.
        var BrakingFriction=(Movement.UseSeparateBrakingFriction?Movement.BrakingFriction:Movement.GroundFriction)*Movement.BrakingFrictionFactor;
        var FovY=2f*MathF.Atan(MathF.Tan(Character.Camera.FieldOfViewDegrees*MathF.PI/360f)/TemplateAspect);
        var Pivot=Character.SpringArm.RelativeLocationCm;
        var Builder=new StringBuilder();
        Builder.AppendLine("using CSharpGameEngine.Entities;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Character;");
        Builder.AppendLine("using CSharpGameEngine.Runtime.Windows;");
        Builder.AppendLine("namespace "+ThirdPersonTemplateImporter.Namespace+";");
        Builder.AppendLine("/// <summary>");
        Builder.AppendLine("/// UEの"+Character.Blueprint+"の移植。PlayerStartの位置に置く。値はBlueprintのCDO実値をmへ換算したもの。");
        Builder.AppendLine("/// 見た目はスケルタルメッシュ対応(ADR-0066)までの仮のカプセル。カメラの水平FOV "+Character.Camera.FieldOfViewDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)+"°は16:9での垂直FOVへ換算している。");
        Builder.AppendLine("/// </summary>");
        Builder.AppendLine("[StartupScene(Order=2)]");
        Builder.AppendLine("internal partial class ThirdPersonCharacter:IScene {");
        Builder.AppendLine("    public Node Root=>new(){");
        Builder.AppendLine("        Name=\"BP_ThirdPersonCharacter\",");
        Builder.AppendLine("        Components=[");
        Builder.Append("            new Transform { Position=new LogicalVector3(").Append(F(Player.Translation[0])).Append(',').Append(F(Player.Translation[1])).Append(',').Append(F(Player.Translation[2]))
            .Append("),Rotation=new LogicalQuaternion(new LogicalVector3(0f,").Append(F(FacingYawRadians(Player))).AppendLine(",0f)) },");
        Builder.AppendLine("            new CharacterMovement {");
        Builder.Append("                CapsuleRadius=").Append(L(Character.CapsuleRadiusCm/100f)).Append(",CapsuleHalfHeight=").Append(L(Character.CapsuleHalfHeightCm/100f)).AppendLine(",");
        Builder.Append("                MaxWalkSpeed=").Append(L(Movement.MaxWalkSpeedCm/100f)).Append(",MinAnalogWalkSpeed=").Append(L(Movement.MinAnalogWalkSpeedCm/100f)).Append(",MaxAcceleration=").Append(L(Movement.MaxAccelerationCm/100f)).AppendLine(",");
        Builder.Append("                BrakingDecelerationWalking=").Append(L(Movement.BrakingDecelerationWalkingCm/100f)).Append(",BrakingDecelerationFalling=").Append(L(Movement.BrakingDecelerationFallingCm/100f))
            .Append(",GroundFriction=").Append(L(Movement.GroundFriction)).Append(",BrakingFriction=").Append(L(BrakingFriction)).AppendLine(",");
        Builder.Append("                JumpZVelocity=").Append(L(Movement.JumpZVelocityCm/100f)).Append(",AirControl=").Append(L(Movement.AirControl))
            .Append(",AirControlBoostMultiplier=").Append(L(Movement.AirControlBoostMultiplier)).Append(",AirControlBoostVelocityThreshold=").Append(L(Movement.AirControlBoostVelocityThresholdCm/100f)).AppendLine(",");
        Builder.Append("                GravityScale=").Append(L(Movement.GravityScale)).Append(",MaxStepHeight=").Append(L(Movement.MaxStepHeightCm/100f)).Append(",WalkableFloorAngleDegrees=").Append(L(Movement.WalkableFloorAngleDegrees)).AppendLine(",");
        Builder.Append("                RotationRateDegrees=").Append(L(Movement.RotationRateYawDegrees)).Append(",OrientRotationToMovement=").Append(Movement.OrientRotationToMovement?"true":"false").AppendLine(",");
        Builder.AppendLine("            },");
        Builder.AppendLine("            new CharacterMovementState(),");
        Builder.AppendLine("            new CharacterInput(),");
        Builder.Append("            new PlayerController { ControlYawDegrees=").Append(F(FacingYawRadians(Player)*180f/MathF.PI)).AppendLine(" },");
        Builder.AppendLine("        ],");
        Builder.AppendLine("        Children=[");
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"CapsuleVisual\",");
        Builder.AppendLine("                Components=[");
        // The unit capsule primitive has radius 0.5 and total height 2.
        Builder.Append("                    new Transform { Scale=new LogicalVector3(").Append(F(Character.CapsuleRadiusCm/50f)).Append(',').Append(F(Character.CapsuleHalfHeightCm/100f)).Append(',').Append(F(Character.CapsuleRadiusCm/50f)).AppendLine(") },");
        Builder.AppendLine("                    new PrimitiveMesh { Type=PrimitiveType.Capsule },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"FacingIndicator\",");
        Builder.AppendLine("                Components=[");
        Builder.Append("                    new Transform { Position=new LogicalVector3(0f,").Append(F(Character.CapsuleHalfHeightCm/200f)).Append(',').Append(F(Character.CapsuleRadiusCm/100f)).AppendLine("),Scale=new LogicalVector3(0.1f,0.1f,0.3f) },");
        Builder.AppendLine("                    new PrimitiveMesh { Type=PrimitiveType.Box },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        Builder.AppendLine("            new Node {");
        Builder.AppendLine("                Name=\"FollowCamera\",");
        Builder.AppendLine("                Components=[");
        Builder.AppendLine("                    new Transform(),");
        Builder.Append("                    new SpringArm { PivotOffset=new LogicalVector3(").Append(F(Pivot[1]/100f)).Append(',').Append(F(Pivot[2]/100f)).Append(',').Append(F(Pivot[0]/100f))
            .Append("),TargetArmLength=").Append(L(Arm.TargetArmLengthCm/100f)).Append(",ProbeRadius=").Append(L(Arm.ProbeSizeCm/100f)).Append(",DoCollisionTest=").Append(Arm.DoCollisionTest?"true":"false").AppendLine(" },");
        Builder.Append("                    new Camera { Active=true,FovYRadians=").Append(F(FovY)).AppendLine(",FarZ=1000f },");
        Builder.AppendLine("                ],");
        Builder.AppendLine("            },");
        Builder.AppendLine("        ],");
        Builder.AppendLine("    };");
        Builder.AppendLine("}");
        return Builder.ToString();
    }
    internal static string BuildInputSource(JsonElement Input){
        var Contexts=Input.GetProperty("contexts").EnumerateArray().ToArray();
        var Actions=Input.GetProperty("actions").EnumerateObject().Select(Property=>(Name:Property.Name,Type:Property.Value.GetString()??string.Empty)).OrderBy(Item=>Item.Name,StringComparer.Ordinal).ToArray();
        foreach(var (Name,Type) in Actions){
            if(Type is not ("BOOLEAN" or "AXIS2D")){
                throw new NotSupportedException($"Input action {Name} has unsupported value type {Type}.");
            }
        }
        var Builder=new StringBuilder();
        Builder.AppendLine("using CSharpGameEngine.Core;");
        Builder.AppendLine("using CSharpGameEngine.Input;");
        Builder.AppendLine("using CSharpGameEngine.Platform.Windows;");
        Builder.AppendLine("namespace "+InputNamespace+";");
        Builder.AppendLine("/// <summary>");
        Builder.AppendLine("/// UEのEnhanced Input(" + string.Join("、",Contexts.Select(Context=>Context.GetProperty("name").GetString()))+")の移植。キー名はUEの名前、");
        Builder.AppendLine("/// Mouse2Dはraw inputのマウス移動量(Delta)に対応する。modifierはUEと同じ順に適用される。");
        Builder.AppendLine("/// </summary>");
        Builder.AppendLine("public static class ThirdPersonInput {");
        foreach(var Context in Contexts){
            var Name=Context.GetProperty("name").GetString()!;
            Builder.Append("    public static readonly InputContextId ").Append(Name).Append("=new(StableId.Parse(\"").Append(StableId.FromName(InputIdNamespace,"context/"+Name)).AppendLine("\"));");
        }
        foreach(var (Name,_) in Actions){
            Builder.Append("    public static readonly InputActionId ").Append(Name).Append("=new(StableId.Parse(\"").Append(StableId.FromName(InputIdNamespace,"action/"+Name)).AppendLine("\"));");
        }
        Builder.AppendLine("    public static InputServer CreateServer(){");
        Builder.AppendLine("        static InputControl Key(string Code)=>new(InputDeviceKind.Keyboard,Code);");
        Builder.AppendLine("        static InputControl Button(string Code)=>new(InputDeviceKind.Gamepad,Code);");
        Builder.AppendLine("        var Server=new InputServer(new InputActionMap([");
        foreach(var Context in Contexts){
            var Name=Context.GetProperty("name").GetString()!;
            Builder.Append("            new InputContext(").Append(Name).Append(",\"").Append(Name).AppendLine("\",0,[");
            foreach(var Mapping in Context.GetProperty("mappings").EnumerateArray()){
                Builder.Append("                ").Append(Binding(Mapping)).AppendLine(",");
            }
            Builder.AppendLine("            ]),");
        }
        Builder.AppendLine("        ]));");
        foreach(var Context in Contexts){
            Builder.Append("        Server.SetContextActive(").Append(Context.GetProperty("name").GetString()).AppendLine(",true,default);");
        }
        Builder.AppendLine("        return Server;");
        Builder.AppendLine("    }");
        Builder.AppendLine("}");
        return Builder.ToString();
    }
    private static string Binding(JsonElement Mapping){
        var Action=Mapping.GetProperty("action").GetString()!;
        var Key=Mapping.GetProperty("key").GetString()!;
        var Control=Key switch {
            "Mouse2D"=>"new InputControl(InputDeviceKind.Mouse,\"Delta\",InputControlKind.RelativeAxis2D)",
            "Gamepad_Left2D"=>"Win32InputEventAdapter.GamepadLeftStick",
            "Gamepad_Right2D"=>"Win32InputEventAdapter.GamepadRightStick",
            _ when Key.StartsWith("Gamepad_",StringComparison.Ordinal)=>"Button(\""+Escape(Key)+"\")",
            _ when Key.Length==1&&char.IsAsciiLetterUpper(Key[0])=>"Key(\""+Key+"\")",
            "SpaceBar" or "Up" or "Down" or "Left" or "Right"=>"Key(\""+Key+"\")",
            _=>throw new NotSupportedException($"Unreal key {Key} has no Win32 input control mapping."),
        };
        var Arguments=new List<string>();
        var Modifiers=new List<string>();
        foreach(var Modifier in Mapping.GetProperty("modifiers").EnumerateArray()){
            var Kind=Modifier.GetProperty("kind").GetString();
            switch(Kind){
                case "DeadZone":
                    if(Modifiers.Count>0||Modifier.GetProperty("upper").GetSingle()!=1f||Modifier.GetProperty("type").GetString()!="RADIAL"){
                        throw new NotSupportedException($"{Action}/{Key}: only a leading radial dead zone with upper bound 1 is supported.");
                    }
                    Arguments.Add("DeadZone:"+F(Modifier.GetProperty("lower").GetSingle()));
                    break;
                case "Scalar":
                    var Scalar=Modifier.GetProperty("scalar").EnumerateArray().Select(Value=>Value.GetSingle()).ToArray();
                    if(Modifiers.Count>0||Scalar[0]!=Scalar[1]){
                        throw new NotSupportedException($"{Action}/{Key}: only a leading uniform scalar is supported.");
                    }
                    if(Scalar[0]!=1f){
                        Arguments.Add("Scale:"+F(Scalar[0]));
                    }
                    break;
                case "SwizzleAxis":
                    if(Modifier.GetProperty("order").GetString()!="YXZ"){
                        throw new NotSupportedException($"{Action}/{Key}: only the YXZ swizzle is supported.");
                    }
                    Modifiers.Add("new InputSwizzleModifier()");
                    break;
                case "Negate":
                    var X=Modifier.GetProperty("x").GetBoolean();
                    var Y=Modifier.GetProperty("y").GetBoolean();
                    Modifiers.Add(X&&Y?"new InputNegateModifier()":$"new InputNegateModifier(X:{(X?"true":"false")},Y:{(Y?"true":"false")})");
                    break;
                default:
                    throw new NotSupportedException($"{Action}/{Key}: Unreal input modifier {Kind} is not supported.");
            }
        }
        if(Modifiers.Count>0){
            Arguments.Add("Modifiers:["+string.Join(",",Modifiers)+"]");
        }
        return "new InputBinding("+Action+","+Control+(Arguments.Count>0?","+string.Join(",",Arguments):string.Empty)+")";
    }
    /// <summary>LogicalScalar fields take float literals through the implicit conversion the Scene DSL allows.</summary>
    private static string L(float Value)=>F(Value);
    /// <summary>
    /// Engine yaw (turning +F toward +R) that faces an Unreal actor's forward. The level convention
    /// maps Unreal (X, Y, Z) to engine (R, U, -F): Unreal's forward +X is engine +R, and an Unreal
    /// yaw ψ turns the same way as an engine yaw ψ. The manifest's yawRadians is -ψ, so the facing
    /// yaw is ψ + 90° = 90° - yawRadians.
    /// </summary>
    internal static float FacingYawRadians(ImportPlayerStart Player)=>MathF.PI/2f-Player.YawRadians;
}
internal sealed class ThirdPersonCharacterManifest {
    public string Blueprint { get; set; }=string.Empty;
    public float CapsuleRadiusCm { get; set; }
    public float CapsuleHalfHeightCm { get; set; }
    public ThirdPersonMovementManifest Movement { get; set; }=new();
    public ThirdPersonMeshManifest Mesh { get; set; }=new();
    public ThirdPersonSpringArmManifest SpringArm { get; set; }=new();
    public ThirdPersonCameraManifest Camera { get; set; }=new();
    public List<ThirdPersonAnimationManifest>Animations { get; set; }=[];
}
internal sealed class ThirdPersonMovementManifest {
    public bool OrientRotationToMovement { get; set; }
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
    public float WalkableFloorAngleDegrees { get; set; }
}
internal sealed class ThirdPersonSpringArmManifest {
    public float[]RelativeLocationCm { get; set; }=[0f,0f,0f];
    public float TargetArmLengthCm { get; set; }
    public bool UsePawnControlRotation { get; set; }
    public bool DoCollisionTest { get; set; }
    public float ProbeSizeCm { get; set; }
    public float[]SocketOffsetCm { get; set; }=[0f,0f,0f];
    public float[]TargetOffsetCm { get; set; }=[0f,0f,0f];
    public bool EnableCameraLag { get; set; }
}
internal sealed class ThirdPersonCameraManifest {
    public float FieldOfViewDegrees { get; set; }
    public bool UsePawnControlRotation { get; set; }
}
