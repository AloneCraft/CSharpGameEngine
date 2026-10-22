using System.Numerics;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// A skinned glTF drawn at this node (ADR-0066). The file must contain exactly one skin; its skinned
/// primitives follow the skeleton's pose and its other primitives keep their glTF placement.
/// Without an animator the mesh stays in its bind pose.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,2)]
public struct SkeletalMesh {
    public string AssetPath;
}
/// <summary>The skeleton and the latest skinning palette of a <see cref="SkeletalMesh"/>.</summary>
public struct SkeletalMeshRuntime:IRuntimeComponent<SkeletalMesh> {
    public Skeleton?Skeleton;
    /// <summary>Inverse bind * joint model per joint (row vectors); identity until an animator writes a pose.</summary>
    public Matrix4x4[]?Palette;
    public Matrix4x4[]?Model;
    public string?Error;
}
/// <summary>
/// Unreal ThirdPerson's unarmed locomotion graph (<see cref="LocomotionStateMachine"/>, ABP_Unarmed) on a <see cref="SkeletalMesh"/> node.
/// Gameplay writes the inputs each frame (<see cref="Speed"/>, <see cref="Direction"/>, <see cref="ShouldMove"/>, <see cref="Falling"/>,
/// <see cref="VerticalSpeed"/>; the engine's <c>LocomotionObserver</c> does it from the character's movement, as the Blueprint's
/// BlueprintUpdateAnimation does). The speeds are the blend space's sample points (m/s). <see cref="Walk"/> and <see cref="Jog"/> are the
/// forward clips; the other seven directions of each are optional, and a direction without a clip uses the forward one.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,3)]
public struct Locomotion() {
    public string Idle="";
    public string Walk="";
    public string WalkForwardRight="";
    public string WalkRight="";
    public string WalkBackwardRight="";
    public string WalkBackward="";
    public string WalkBackwardLeft="";
    public string WalkLeft="";
    public string WalkForwardLeft="";
    public string Jog="";
    public string JogForwardRight="";
    public string JogRight="";
    public string JogBackwardRight="";
    public string JogBackward="";
    public string JogBackwardLeft="";
    public string JogLeft="";
    public string JogForwardLeft="";
    public string Jump="";
    public string Fall="";
    public string Land="";
    public float WalkSpeed=3f;
    public float JogSpeed=6f;
    /// <summary>The blend space's target weight interpolation speed per second (UE's TargetWeightInterpolationSpeedPerSec).</summary>
    public float BlendWeightSpeed=LocomotionStateMachine.WeightInterpolationSpeed;
    /// <summary>Horizontal ground speed this frame (m/s).</summary>
    public float Speed=0f;
    /// <summary>Signed angle in degrees (−180 to 180) from the body's forward to the velocity, positive toward the side the Right clips travel to (+R when facing +F).</summary>
    public float Direction=0f;
    /// <summary>Moving and being pushed: the ground speed is above 0.01 cm/s and the character has an acceleration.</summary>
    public bool ShouldMove=false;
    public bool Falling=false;
    /// <summary>Upward speed this frame (m/s); positive while rising.</summary>
    public float VerticalSpeed=0f;
    /// <summary>The walk clip of direction <paramref name="Direction"/> (<see cref="LocomotionDirection"/>); the forward clip when that one is not named.</summary>
    public readonly string WalkClip(int Direction)=>Direction switch {
        LocomotionDirection.ForwardRight=>Pick(this.WalkForwardRight,this.Walk),
        LocomotionDirection.Right=>Pick(this.WalkRight,this.Walk),
        LocomotionDirection.BackwardRight=>Pick(this.WalkBackwardRight,this.Walk),
        LocomotionDirection.Backward=>Pick(this.WalkBackward,this.Walk),
        LocomotionDirection.BackwardLeft=>Pick(this.WalkBackwardLeft,this.Walk),
        LocomotionDirection.Left=>Pick(this.WalkLeft,this.Walk),
        LocomotionDirection.ForwardLeft=>Pick(this.WalkForwardLeft,this.Walk),
        _=>this.Walk??string.Empty,
    };
    /// <summary>The jog clip of direction <paramref name="Direction"/>; the forward clip when that one is not named.</summary>
    public readonly string JogClip(int Direction)=>Direction switch {
        LocomotionDirection.ForwardRight=>Pick(this.JogForwardRight,this.Jog),
        LocomotionDirection.Right=>Pick(this.JogRight,this.Jog),
        LocomotionDirection.BackwardRight=>Pick(this.JogBackwardRight,this.Jog),
        LocomotionDirection.Backward=>Pick(this.JogBackward,this.Jog),
        LocomotionDirection.BackwardLeft=>Pick(this.JogBackwardLeft,this.Jog),
        LocomotionDirection.Left=>Pick(this.JogLeft,this.Jog),
        LocomotionDirection.ForwardLeft=>Pick(this.JogForwardLeft,this.Jog),
        _=>this.Jog??string.Empty,
    };
    private static string Pick(string?Named,string?Forward)=>string.IsNullOrEmpty(Named)?Forward??string.Empty:Named;
}
public struct LocomotionRuntime:IRuntimeComponent<Locomotion> {
    public LocomotionStateMachine?Machine;
    public JointPose[]?Pose;
    public string?Error;
}
