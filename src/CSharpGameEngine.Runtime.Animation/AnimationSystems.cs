using System.Diagnostics;
using System.Numerics;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>Loads each SkeletalMesh's skeleton and starts it in the bind pose (identity palette).</summary>
[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(11)]
internal struct SkeletalMeshSetup:ISystem {
    public bool Update(ref SkeletalMesh Mesh,ref SkeletalMeshRuntime Runtime,ref State State){
        if(Runtime.Skeleton is not null||Runtime.Error is not null){
            return true;
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
        return true;
    }
}
/// <summary>
/// Evaluates <see cref="LocomotionAnimator"/> once per presented frame on wall-clock time and writes
/// the skinning palette. Presentation data: the pose never feeds back into the simulation.
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(12)]
internal struct LocomotionAnimation:ISystem {
    /// <summary>Longest step one frame may advance the animation, so a hitch does not skip whole states.</summary>
    private const float MaximumDeltaSeconds=0.1f;
    public bool Update(ref SkeletalMeshRuntime Mesh,ref LocomotionAnimator Animator,ref LocomotionAnimatorRuntime Runtime,ref State State){
        if(Mesh.Skeleton is not{ }Skeleton||Mesh.Palette is null||Mesh.Model is null||Runtime.Error is not null){
            return true;
        }
        if(Runtime.Machine is null){
            try {
                var Clips=new LocomotionClips(
                    SkeletalAssetCache.Clip(Animator.IdleClip,Skeleton),SkeletalAssetCache.Clip(Animator.WalkClip,Skeleton),SkeletalAssetCache.Clip(Animator.JogClip,Skeleton),
                    SkeletalAssetCache.Clip(Animator.JumpClip,Skeleton),SkeletalAssetCache.Clip(Animator.FallClip,Skeleton),SkeletalAssetCache.Clip(Animator.LandClip,Skeleton));
                Runtime.Machine=new LocomotionStateMachine(Skeleton,Clips,Animator.WalkSpeed,Animator.JogSpeed,Animator.BlendSeconds);
                Runtime.Pose=new JointPose[Skeleton.JointCount];
                Runtime.LastTimestamp=Stopwatch.GetTimestamp();
            }
            catch(Exception Ex) when(Ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException){
                Runtime.Error=Ex.Message;
                return true;
            }
        }
        if(Runtime.Machine is not{ }Machine||Runtime.Pose is not{ }Pose){
            return true;
        }
        var Now=Stopwatch.GetTimestamp();
        var DeltaSeconds=Math.Min((Now-Runtime.LastTimestamp)/(float)Stopwatch.Frequency,MaximumDeltaSeconds);
        Runtime.LastTimestamp=Now;
        Machine.Update(DeltaSeconds,Animator.Speed,Animator.Falling,Animator.VerticalSpeed,Pose);
        Skeleton.ComputePalette(Pose,Mesh.Model,Mesh.Palette);
        return true;
    }
}
