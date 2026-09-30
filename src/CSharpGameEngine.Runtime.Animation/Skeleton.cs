using System.Numerics;
using CSharpGameEngine.Import.Gltf;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>A joint's local transform. Blending lerps translation and scale and nlerps rotation along the shorter arc.</summary>
public struct JointPose {
    public Vector3 Translation;
    public Quaternion Rotation;
    public Vector3 Scale;
    public readonly Matrix4x4 ToMatrix()=>Matrix4x4.CreateScale(this.Scale)*Matrix4x4.CreateFromQuaternion(this.Rotation)*Matrix4x4.CreateTranslation(this.Translation);
    public static JointPose Blend(in JointPose From,in JointPose To,float Weight)=>new() {
        Translation=Vector3.Lerp(From.Translation,To.Translation,Weight),
        Rotation=Quaternion.Lerp(From.Rotation,To.Rotation,Weight),
        Scale=Vector3.Lerp(From.Scale,To.Scale,Weight),
    };
    public static void Blend(ReadOnlySpan<JointPose>From,ReadOnlySpan<JointPose>To,float Weight,Span<JointPose>Result){
        for(var Joint=0;Joint<Result.Length;Joint++){
            Result[Joint]=Blend(From[Joint],To[Joint],Weight);
        }
    }
}
/// <summary>
/// The joint hierarchy of an imported skin (ADR-0066), in engine space. <see cref="ComputePalette"/>
/// turns local joint poses into skinning matrices: <c>InverseBind[j] * Model[j]</c> (row vectors).
/// </summary>
public sealed class Skeleton {
    private readonly Dictionary<string,int>IndexByName;
    private readonly int[]Parents;
    private readonly int[]EvaluationOrder;
    private readonly JointPose[]RestPose;
    private readonly Matrix4x4[]InverseBind;
    private readonly Matrix4x4 Root;
    private Skeleton(string[]Names,int[]Parents,JointPose[]Rest,Matrix4x4[]InverseBind,Matrix4x4 Root){
        this.JointNames=Names;
        this.Parents=Parents;
        this.RestPose=Rest;
        this.InverseBind=InverseBind;
        this.Root=Root;
        this.IndexByName=[];
        for(var Joint=0;Joint<Names.Length;Joint++){
            this.IndexByName.TryAdd(Names[Joint],Joint);
        }
        this.EvaluationOrder=ParentsFirst(Parents);
    }
    public IReadOnlyList<string>JointNames { get; }
    public int JointCount=>this.JointNames.Count;
    public ReadOnlySpan<JointPose>Rest=>this.RestPose;
    public int ParentOf(int Joint)=>this.Parents[Joint];
    public int IndexOf(string Name)=>this.IndexByName.TryGetValue(Name,out var Index)?Index:-1;
    public static Skeleton FromImported(ImportedSkin Skin){
        ArgumentNullException.ThrowIfNull(Skin);
        var Count=Skin.JointNames.Count;
        var Rest=new JointPose[Count];
        for(var Joint=0;Joint<Count;Joint++){
            Rest[Joint]=new JointPose { Translation=Skin.RestTranslations[Joint],Rotation=Skin.RestRotations[Joint],Scale=Skin.RestScales[Joint] };
        }
        return new Skeleton([.. Skin.JointNames],[.. Skin.ParentIndices],Rest,[.. Skin.InverseBindMatrices],Skin.SkeletonTransform);
    }
    /// <summary>Writes each joint's model matrix and skinning matrix for <paramref name="Local"/>.</summary>
    public void ComputePalette(ReadOnlySpan<JointPose>Local,Span<Matrix4x4>Model,Span<Matrix4x4>Palette){
        if(Local.Length<this.JointCount||Model.Length<this.JointCount||Palette.Length<this.JointCount){
            throw new ArgumentException($"The skeleton has {this.JointCount} joints.");
        }
        foreach(var Joint in this.EvaluationOrder){
            var Parent=this.Parents[Joint];
            Model[Joint]=Local[Joint].ToMatrix()*(Parent<0?this.Root:Model[Parent]);
            Palette[Joint]=this.InverseBind[Joint]*Model[Joint];
        }
    }
    /// <summary>Joint indices ordered so every parent precedes its children; a cycle throws.</summary>
    private static int[]ParentsFirst(int[]Parents){
        var Order=new List<int>(Parents.Length);
        var State=new byte[Parents.Length];
        void Visit(int Joint){
            if(State[Joint]==2){
                return;
            }
            if(State[Joint]==1){
                throw new InvalidDataException("The skin's joint hierarchy has a cycle.");
            }
            State[Joint]=1;
            if(Parents[Joint]>=0){
                Visit(Parents[Joint]);
            }
            State[Joint]=2;
            Order.Add(Joint);
        }
        for(var Joint=0;Joint<Parents.Length;Joint++){
            Visit(Joint);
        }
        return [.. Order];
    }
}
