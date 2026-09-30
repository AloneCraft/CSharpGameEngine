using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeThirdPerson.Scenes;
/// <summary>
/// UEの/Game/ThirdPerson/Blueprints/BP_ThirdPersonCharacter.BP_ThirdPersonCharacter_Cの移植。PlayerStartの位置に置く。値はBlueprintのCDO実値をmへ換算したもの。
/// 見た目はスケルタルメッシュ対応(ADR-0066)までの仮のカプセル。カメラの水平FOV 90°は16:9での垂直FOVへ換算している。
/// </summary>
[StartupScene(Order=2)]
internal partial class ThirdPersonCharacter:IScene {
    public Node Root=>new(){
        Name="BP_ThirdPersonCharacter",
        Components=[
            new Transform { Position=new LogicalVector3(0f,3.0201263f,0f),Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707964f,0f)) },
            new CharacterMovement {
                CapsuleRadius=0.35f,CapsuleHalfHeight=0.9f,
                MaxWalkSpeed=6f,MinAnalogWalkSpeed=0.2f,MaxAcceleration=15f,
                BrakingDecelerationWalking=20f,BrakingDecelerationFalling=15f,GroundFriction=8f,BrakingFriction=0f,
                JumpZVelocity=5f,AirControl=0.35f,AirControlBoostMultiplier=2f,AirControlBoostVelocityThreshold=0.25f,
                GravityScale=1f,MaxStepHeight=0.45f,WalkableFloorAngleDegrees=44.765083f,
                RotationRateDegrees=500f,OrientRotationToMovement=true,
            },
            new CharacterMovementState(),
            new CharacterInput(),
            new PlayerController { ControlYawDegrees=90f },
        ],
        Children=[
            new Node {
                Name="CapsuleVisual",
                Components=[
                    new Transform { Scale=new LogicalVector3(0.7f,0.9f,0.7f) },
                    new PrimitiveMesh { Type=PrimitiveType.Capsule },
                ],
            },
            new Node {
                Name="FacingIndicator",
                Components=[
                    new Transform { Position=new LogicalVector3(0f,0.45f,0.35f),Scale=new LogicalVector3(0.1f,0.1f,0.3f) },
                    new PrimitiveMesh { Type=PrimitiveType.Box },
                ],
            },
            new Node {
                Name="FollowCamera",
                Components=[
                    new Transform(),
                    new SpringArm { PivotOffset=new LogicalVector3(0f,0.08492264f,0f),TargetArmLength=4f,ProbeRadius=0.12f,DoCollisionTest=true },
                    new Camera { Active=true,FovYRadians=1.024779f,FarZ=1000f },
                ],
            },
        ],
    };
}
