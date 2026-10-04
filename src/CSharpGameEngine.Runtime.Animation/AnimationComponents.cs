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
/// Unreal ThirdPerson's unarmed locomotion graph (<see cref="LocomotionStateMachine"/>) on a
/// <see cref="SkeletalMesh"/> node. Gameplay writes <see cref="Speed"/>, <see cref="Falling"/> and
/// <see cref="VerticalSpeed"/> each frame; the speeds are the blend space's sample points (m/s).
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,3)]
public struct LocomotionAnimator() {
    public string IdleClip="";
    public string WalkClip="";
    public string JogClip="";
    public string JumpClip="";
    public string FallClip="";
    public string LandClip="";
    public float WalkSpeed=3f;
    public float JogSpeed=6f;
    public float BlendSeconds=0.2f;
    /// <summary>Horizontal ground speed this frame (m/s).</summary>
    public float Speed=0f;
    public bool Falling=false;
    /// <summary>Upward speed this frame (m/s); positive while rising.</summary>
    public float VerticalSpeed=0f;
}
public struct LocomotionAnimatorRuntime:IRuntimeComponent<LocomotionAnimator> {
    public LocomotionStateMachine?Machine;
    public JointPose[]?Pose;
    public string?Error;
}
