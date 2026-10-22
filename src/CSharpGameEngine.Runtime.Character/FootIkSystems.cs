using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Runtime.Character;
/// <summary>
/// Puts a character's feet on the ground (CR_Mannequin_FootIK, ADR-0078/M0107; see <see cref="FootIk"/>). Runs after the locomotion pose
/// is made and before the palette goes to the renderer: it traces the ground under both feet in the physics world, smooths the offsets, edits the
/// pose and recomputes the skinning palette. Presentation data: nothing here feeds back into the simulation.
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(LocomotionAnimation))]
internal struct FootIkUpdate:ISystem {
    private const float MaximumDeltaSeconds=0.1f;
    private List<PhysicsRayHit>?Hits;
    public void Update(Entity Entity,ref SkeletalMeshRuntime Mesh,ref LocomotionRuntime Motion,ref Locomotion Animator,ref FootIk Settings,ref FootIkRuntime Runtime,IPhysicsHost Host,ref State State){
        if(Runtime.Error is not null||Mesh.Skeleton is not{ }Skeleton||Mesh.Model is null||Mesh.Palette is null||Motion.Pose is not{ }Pose){
            return;
        }
        if(Runtime.Pose is null){
            try {
                Runtime.Pose=new FootIkPose(Skeleton,Settings);
                Runtime.Scratch=new Matrix4x4[Skeleton.JointCount];
            }
            catch(ArgumentException Ex){
                // A skeleton without the named joints keeps its animated pose; the component says why.
                Runtime.Error=Ex.Message;
                return;
            }
        }
        var Ik=Runtime.Pose;
        var Tracing=!Animator.Falling;
        float?Left=null;
        float?Right=null;
        if(Tracing&&RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out FixedAffineTransform World,out var Frame)==FixedTransformResolveStatus.Success&&Frame==Host.Physics.Frame){
            var Scratch=this.Hits??=[];
            Left=GroundHeight(Scratch,Host.Physics.Active.MutableWorld,World,Ik.TraceOrigin(Mesh.Model,FootIkPose.Left),Settings.TraceTop,Settings.TraceBottom);
            Right=GroundHeight(Scratch,Host.Physics.Active.MutableWorld,World,Ik.TraceOrigin(Mesh.Model,FootIkPose.Right),Settings.TraceTop,Settings.TraceBottom);
        }
        Ik.Update(Math.Min(State.FrameDeltaSeconds,MaximumDeltaSeconds),Tracing,Left,Right);
        if(Ik.IsNeutral){
            return;
        }
        Ik.Apply(Pose,Runtime.Scratch!);
        Skeleton.ComputePalette(Pose,Mesh.Model,Mesh.Palette);
    }
    /// <summary>
    /// The height of the ground below a model-space ray start, relative to the mesh origin (the node's origin: UE's component origin); null when
    /// the ray finds nothing within the trace length or a number cannot be represented.
    /// </summary>
    internal static float?GroundHeight(List<PhysicsRayHit>Results,DeterministicPhysicsWorld Physics,in FixedAffineTransform World,Vector3 Origin,float TraceTop,float TraceBottom){
        if(!LogicalScalar.TryFromSingle(Origin.X,out var R)||!LogicalScalar.TryFromSingle(Origin.Y,out var U)||!LogicalScalar.TryFromSingle(Origin.Z,out var F)
            ||!World.Logical.TryTransformPoint(new LogicalVector3(R,U,F),out var Start)){
            return null;
        }
        var Length=TraceTop+TraceBottom;
        if(!(Length>0f)||!LogicalScalar.TryFromSingle(Length,out var Distance)){
            return null;
        }
        Results.Clear();
        PhysicsQueryResult Result;
        try {
            Result=Physics.Raycast(new PhysicsRay(Start,new LogicalVector3(LogicalScalar.Zero,-LogicalScalar.One,LogicalScalar.Zero),Distance),PhysicsLayerMask.All,Results);
        }
        catch(OverflowException){
            return null;
        }
        if(Result.Status!=PhysicsQueryStatus.Success||Results.Count==0){
            return null;
        }
        var Nearest=Results[0];
        foreach(var Candidate in Results){
            if(Candidate.Distance<Nearest.Distance){
                Nearest=Candidate;
            }
        }
        return (Nearest.Point.U-World.Logical.Translation.U).TryToSingle(out var Height)?Height:null;
    }
}
/// <summary>
/// The first-person arms follow the body (ABP_FP_Copy and CtrlRig_FPWarp, ADR-0078/M0107; see <see cref="FirstPersonPose"/>). The mesh with a <see cref="FirstPersonPose"/>
/// is a child of the body's mesh: it takes the body's final pose (after the foot IK), warps it with <see cref="FirstPersonRig"/> and computes its own skinning palette.
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(FootIkUpdate))]
internal struct FirstPersonPoseUpdate:ISystem {
    public void Update(ref SkeletalMeshRuntime Mesh,ref FirstPersonPose Settings,ref FirstPersonPoseRuntime Runtime,in Parent<LocomotionRuntime> Body,ref State State){
        if(Runtime.Error is not null||Mesh.Skeleton is not{ }Skeleton||Mesh.Model is null||Mesh.Palette is null||Body.Value.Pose is not{ }Source){
            return;
        }
        if(Runtime.Rig is null){
            try {
                Runtime.Rig=new FirstPersonRig(Skeleton,Settings);
                Runtime.Pose=new JointPose[Skeleton.JointCount];
                Runtime.Scratch=new Matrix4x4[Skeleton.JointCount];
            }
            catch(ArgumentException Ex){
                // A skeleton without the named joints keeps its bind pose; the component says why.
                Runtime.Error=Ex.Message;
                return;
            }
        }
        if(Source.Length<Skeleton.JointCount){
            return;
        }
        var Pose=Runtime.Pose!;
        Source.AsSpan(0,Skeleton.JointCount).CopyTo(Pose);
        Runtime.Rig.Apply(Pose,Runtime.Scratch!);
        Skeleton.ComputePalette(Pose,Mesh.Model,Mesh.Palette);
    }
}
