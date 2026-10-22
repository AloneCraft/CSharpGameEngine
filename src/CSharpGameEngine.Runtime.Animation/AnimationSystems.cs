using System.Numerics;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>Loads each SkeletalMesh's skeleton and starts it in the bind pose (identity palette).</summary>
[SystemPhase(SystemPhase.Presentation)]
internal struct SkeletalMeshSetup:ISystem {
    public void Update(ref SkeletalMesh Mesh,ref SkeletalMeshRuntime Runtime,ref State State){
        if(Runtime.Skeleton is not null||Runtime.Error is not null){
            return;
        }
        try {
            var Skeleton=SkeletalAssetCache.Skeleton(Mesh.AssetPath??string.Empty);
            Runtime.Palette=[.. Enumerable.Repeat(Matrix4x4.Identity,Skeleton.JointCount)];
            Runtime.Model=new Matrix4x4[Skeleton.JointCount];
            Runtime.Skeleton=Skeleton;
        }
        catch(Exception Ex) when(Ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException){
            // A broken asset is reported on the component; the mesh keeps drawing its bind pose.
            Runtime.Error=Ex.Message;
        }
    }
}
/// <summary>
/// Evaluates <see cref="Locomotion"/> once per presented frame on wall-clock time and writes
/// the skinning palette. Presentation data: the pose never feeds back into the simulation.
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(SkeletalMeshSetup))]
internal struct LocomotionAnimation:ISystem {
    /// <summary>Longest step one frame may advance the animation, so a hitch does not skip whole states.</summary>
    private const float MaximumDeltaSeconds=0.1f;
    public void Update(ref SkeletalMeshRuntime Mesh,ref Locomotion Animator,ref LocomotionRuntime Runtime,ref State State){
        if(Mesh.Skeleton is not{ }Skeleton||Mesh.Palette is null||Mesh.Model is null||Runtime.Error is not null){
            return;
        }
        if(Runtime.Machine is null){
            try {
                var Clips=new LocomotionClips(
                    SkeletalAssetCache.Clip(Animator.Idle,Skeleton),SkeletalAssetCache.Clip(Animator.Walk,Skeleton),SkeletalAssetCache.Clip(Animator.Jog,Skeleton),
                    SkeletalAssetCache.Clip(Animator.Jump,Skeleton),SkeletalAssetCache.Clip(Animator.Fall,Skeleton),SkeletalAssetCache.Clip(Animator.Land,Skeleton));
                Runtime.Machine=new LocomotionStateMachine(Skeleton,Clips,Animator.WalkSpeed,Animator.JogSpeed,Animator.BlendSeconds);
                Runtime.Pose=new JointPose[Skeleton.JointCount];
            }
            catch(Exception Ex) when(Ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException){
                Runtime.Error=Ex.Message;
                return;
            }
        }
        if(Runtime.Machine is not{ }Machine||Runtime.Pose is not{ }Pose){
            return;
        }
        var DeltaSeconds=Math.Min(State.FrameDeltaSeconds,MaximumDeltaSeconds);
        Machine.Update(DeltaSeconds,Animator.Speed,Animator.Falling,Animator.VerticalSpeed,Pose);
        Skeleton.ComputePalette(Pose,Mesh.Model,Mesh.Palette);
    }
}
