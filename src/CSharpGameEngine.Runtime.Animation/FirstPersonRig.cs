using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// The first-person arms' animation (ABP_FP_Copy with the control rig CtrlRig_FPWarp, ADR-0078/M0107) on a <see cref="SkeletalMesh"/> node that is a child of the body's
/// mesh: each frame it copies the body's final pose (UE's CopyPoseFromMesh with the attached parent), then warps it so the arms sit right in front of the camera.
/// The warp is the rig's: the spine (<see cref="SpineStart"/> to <see cref="SpineEnd"/>) is pulled by FABRIK toward <see cref="BodyOffset"/> at half weight so the chest
/// stays out of the camera's way, the head is put where the control <c>Ctrl_Head</c> is, and each upper arm is moved onto its shoulder control (<c>Ctrl_Shoulder_L</c> / <c>_R</c>)
/// keeping its animated orientation, the forearm and hand following rigidly. Those three controls sit exactly on the reference pose's head and upper arms
/// (measured from the rig, M0107), so the reference pose of the skeleton stands in for them.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,5)]
public struct FirstPersonPose() {
    public string SpineStart="spine_01";
    public string SpineEnd="spine_04";
    public string Head="head";
    public string UpperArmLeft="upperarm_l";
    public string UpperArmRight="upperarm_r";
    /// <summary>The <c>BodyOffset</c> control: where the end of the spine is pulled to, in the mesh's model space (m). UE (0, -3.586, 109.51) cm.</summary>
    public LogicalVector3 BodyOffset=new(0f,1.0951f,0.03586f);
    /// <summary>The FABRIK node's Weight: how much of the solved spine replaces the animated one.</summary>
    public float SpineWeight=0.5f;
    /// <summary>The FABRIK node's MaxIterations.</summary>
    public int Iterations=10;
    /// <summary>The FABRIK node's Precision (m): UE's 1 cm.</summary>
    public float Precision=0.01f;
}
public struct FirstPersonPoseRuntime:IRuntimeComponent<FirstPersonPose> {
    public FirstPersonRig?Rig;
    public JointPose[]?Pose;
    public Matrix4x4[]?Scratch;
    public string?Error;
}
/// <summary>The solve of <see cref="FirstPersonPose"/> for one skeleton.</summary>
public sealed class FirstPersonRig {
    private readonly Skeleton Skeleton;
    private readonly int[]Chain;
    private readonly int Head;
    private readonly int[]Arms;
    private readonly Matrix4x4[]RestModel;
    private readonly Vector3 BodyOffset;
    private readonly float SpineWeight;
    private readonly int Iterations;
    private readonly float Precision;
    private readonly bool[]Moved;
    public FirstPersonRig(Skeleton Skeleton,in FirstPersonPose Settings){
        ArgumentNullException.ThrowIfNull(Skeleton);
        this.Skeleton=Skeleton;
        var Start=Find(Skeleton,Settings.SpineStart);
        var End=Find(Skeleton,Settings.SpineEnd);
        // The spine is the chain of parents from the end joint up to the start joint.
        List<int>Joints=[End];
        for(var Joint=End;Joint!=Start;){
            Joint=Skeleton.ParentOf(Joint);
            if(Joint<0){
                throw new ArgumentException($"'{Settings.SpineStart}' is not an ancestor of '{Settings.SpineEnd}'.");
            }
            Joints.Add(Joint);
        }
        Joints.Reverse();
        this.Chain=[.. Joints];
        this.Head=Find(Skeleton,Settings.Head);
        this.Arms=[Find(Skeleton,Settings.UpperArmLeft),Find(Skeleton,Settings.UpperArmRight)];
        this.RestModel=new Matrix4x4[Skeleton.JointCount];
        Skeleton.ComputeModel(Skeleton.Rest,this.RestModel);
        this.BodyOffset=Settings.BodyOffset.TryToSingle(out var Offset)?Offset:Vector3.Zero;
        this.SpineWeight=float.IsFinite(Settings.SpineWeight)?Math.Clamp(Settings.SpineWeight,0f,1f):0f;
        this.Iterations=Math.Max(Settings.Iterations,0);
        this.Precision=float.IsFinite(Settings.Precision)?Math.Max(Settings.Precision,1e-6f):0.01f;
        this.Moved=new bool[Skeleton.JointCount];
    }
    /// <summary>
    /// Warps <paramref name="Local"/> (the body's final local pose, copied): the spine toward the body offset, then the head and the upper arms onto the controls.
    /// <paramref name="Scratch"/> holds at least <see cref="Skeleton.JointCount"/> matrices.
    /// </summary>
    public void Apply(Span<JointPose>Local,Span<Matrix4x4>Scratch){
        var Count=this.Skeleton.JointCount;
        if(Scratch.Length<Count||Local.Length<Count){
            throw new ArgumentException($"The skeleton has {Count} joints.");
        }
        var Model=Scratch[..Count];
        this.Skeleton.ComputeModel(Local,Model);
        Array.Clear(this.Moved);
        this.PullSpine(Model);
        // Ctrl_Head: the head's whole transform, with the joints below it carried along.
        this.Place(Model,this.Head,this.RestModel[this.Head]);
        // Ctrl_Shoulder_L / _R: the translation only; the upper arm keeps its orientation, and the forearm and hand move with it.
        foreach(var Arm in this.Arms){
            var Placed=Model[Arm];
            Placed.Translation=this.RestModel[Arm].Translation;
            this.Place(Model,Arm,Placed);
        }
        this.WriteBack(Local,Model);
    }
    /// <summary>FABRIK along the spine with the first joint fixed, then each bone is turned to point at the next one's new place and carries everything below it.</summary>
    private void PullSpine(Span<Matrix4x4>Model){
        if(this.SpineWeight<=0f||this.Chain.Length<2){
            return;
        }
        var Count=this.Chain.Length;
        var Original=new Vector3[Count];
        var Solved=new Vector3[Count];
        var Lengths=new float[Count-1];
        for(var Index=0;Index<Count;Index++){
            Original[Index]=Model[this.Chain[Index]].Translation;
            Solved[Index]=Original[Index];
        }
        var Reach=0f;
        for(var Index=0;Index<Count-1;Index++){
            Lengths[Index]=Vector3.Distance(Original[Index],Original[Index+1]);
            Reach+=Lengths[Index];
        }
        var Root=Original[0];
        var Target=this.BodyOffset;
        if(Vector3.Distance(Root,Target)>=Reach){
            // Out of reach: the chain stretches straight toward the target.
            var Direction=Vector3.Normalize(Target-Root);
            for(var Index=1;Index<Count;Index++){
                Solved[Index]=Solved[Index-1]+(Direction*Lengths[Index-1]);
            }
        }
        else {
            for(var Iteration=0;Iteration<this.Iterations&&Vector3.Distance(Solved[Count-1],Target)>this.Precision;Iteration++){
                Solved[Count-1]=Target;
                for(var Index=Count-2;Index>=0;Index--){
                    Solved[Index]=Solved[Index+1]+(Vector3.Normalize(Solved[Index]-Solved[Index+1])*Lengths[Index]);
                }
                Solved[0]=Root;
                for(var Index=1;Index<Count;Index++){
                    Solved[Index]=Solved[Index-1]+(Vector3.Normalize(Solved[Index]-Solved[Index-1])*Lengths[Index-1]);
                }
            }
        }
        for(var Index=1;Index<Count;Index++){
            Solved[Index]=Vector3.Lerp(Original[Index],Solved[Index],this.SpineWeight);
        }
        // Turn each bone about its own position so it points at the next joint's new place; the subtree below follows, so the next bone is already in position.
        for(var Index=0;Index<Count-1;Index++){
            var Pivot=Model[this.Chain[Index]].Translation;
            var Current=Model[this.Chain[Index+1]].Translation-Pivot;
            var Wanted=Solved[Index+1]-Solved[Index];
            if(Current.LengthSquared()<1e-10f||Wanted.LengthSquared()<1e-10f){
                continue;
            }
            var Turn=Matrix4x4.CreateTranslation(-Pivot)*TwoBoneIk.FromTo(Vector3.Normalize(Current),Vector3.Normalize(Wanted))*Matrix4x4.CreateTranslation(Pivot);
            this.Transform(Model,this.Chain[Index],Turn,IncludeRoot:true);
        }
    }
    /// <summary>Replaces <paramref name="Joint"/>'s model matrix with <paramref name="Placed"/> and moves everything below it the same way (UE's bPropagateToChildren).</summary>
    private void Place(Span<Matrix4x4>Model,int Joint,Matrix4x4 Placed){
        if(!Matrix4x4.Invert(Model[Joint],out var Inverse)){
            return;
        }
        this.Transform(Model,Joint,Inverse*Placed,IncludeRoot:true);
    }
    /// <summary>Applies a model-space transform (row vectors, applied after the joint's matrix) to <paramref name="Joint"/> and every joint below it.</summary>
    private void Transform(Span<Matrix4x4>Model,int Joint,Matrix4x4 Change,bool IncludeRoot){
        foreach(var Candidate in this.Skeleton.ParentsFirstOrder){
            if((Candidate==Joint&&IncludeRoot)||(Candidate!=Joint&&this.IsDescendant(Candidate,Joint))){
                Model[Candidate]=Model[Candidate]*Change;
                this.Moved[Candidate]=true;
            }
        }
    }
    private bool IsDescendant(int Joint,int Ancestor){
        for(var Parent=this.Skeleton.ParentOf(Joint);Parent>=0;Parent=this.Skeleton.ParentOf(Parent)){
            if(Parent==Ancestor){
                return true;
            }
        }
        return false;
    }
    /// <summary>Turns the changed model matrices back into local translations and rotations (scales stay as animated).</summary>
    private void WriteBack(Span<JointPose>Local,ReadOnlySpan<Matrix4x4>Model){
        foreach(var Joint in this.Skeleton.ParentsFirstOrder){
            if(!this.Moved[Joint]){
                continue;
            }
            var Parent=this.Skeleton.ParentOf(Joint);
            var ParentModel=Parent<0?this.Skeleton.RootTransform:Model[Parent];
            if(!Matrix4x4.Invert(ParentModel,out var ParentInverse)){
                continue;
            }
            var Relative=Model[Joint]*ParentInverse;
            Local[Joint].Translation=Relative.Translation;
            Local[Joint].Rotation=TwoBoneIk.ToQuaternion(TwoBoneIk.RotationOf(Relative));
        }
    }
    private static int Find(Skeleton Skeleton,string Name){
        var Joint=Skeleton.IndexOf(Name??string.Empty);
        if(Joint<0){
            throw new ArgumentException($"The skeleton has no joint named '{Name}'.");
        }
        return Joint;
    }
}
