using System.Numerics;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0066: skeleton palettes, clip sampling and Unreal's unarmed locomotion graph.</summary>
public sealed class AnimationRuntimeTests {
    /// <summary>Root at the origin, child 1 m up, both with a quarter turn baked into the bind pose's inverse.</summary>
    private static Skeleton TwoJoints(){
        var ChildRest=Matrix4x4.CreateTranslation(0f,1f,0f);
        Assert.True(Matrix4x4.Invert(ChildRest,out var ChildInverse));
        return Skeleton.FromImported(new ImportedSkin {
            JointNames=["root","child"],
            ParentIndices=[-1,0],
            RestTranslations=[Vector3.Zero,Vector3.UnitY],
            RestRotations=[Quaternion.Identity,Quaternion.Identity],
            RestScales=[Vector3.One,Vector3.One],
            InverseBindMatrices=[Matrix4x4.Identity,ChildInverse],
            SkeletonTransform=Matrix4x4.Identity,
        });
    }
    private static ImportedKeys<Vector3>Line(float Duration,Vector3 From,Vector3 To,bool Step=false)=>new() { Times=[0f,Duration],Values=[From,To],Step=Step };
    /// <summary>A clip that holds the child joint at x = <paramref name="Marker"/>, so a pose reveals which clips it mixes.</summary>
    private static AnimationClip Marker(Skeleton Skeleton,float Marker,float Duration)=>AnimationClip.Bind(new ImportedAnimationClip {
        Name=$"marker{Marker}",
        DurationSeconds=Duration,
        Tracks=[new ImportedJointTrack { JointName="child",Translation=Line(Duration,new Vector3(Marker,1f,0f),new Vector3(Marker,1f,0f)) }],
    },Skeleton);
    private static float ChildX(ReadOnlySpan<JointPose>Pose)=>Pose[1].Translation.X;
    [Fact]
    public void ComputePalette_IsIdentityAtRestAndFollowsTheParent(){
        var Skeleton=TwoJoints();
        var Pose=Skeleton.Rest.ToArray();
        var Model=new Matrix4x4[2];
        var Palette=new Matrix4x4[2];
        Skeleton.ComputePalette(Pose,Model,Palette);
        Assert.Equal(Matrix4x4.Identity,Palette[0]);
        Assert.Equal(Matrix4x4.Identity,Palette[1]);
        Pose[0].Translation=new Vector3(2f,0f,0f);
        Skeleton.ComputePalette(Pose,Model,Palette);
        Assert.Equal(new Vector3(2f,1f,0f),Model[1].Translation);
        Assert.Equal(new Vector3(2f,2f,0f),Vector3.Transform(new Vector3(0f,2f,0f),Palette[1]));
    }
    [Fact]
    public void Sample_InterpolatesLoopsClampsAndStepsKeys(){
        var Skeleton=TwoJoints();
        var Clip=AnimationClip.Bind(new ImportedAnimationClip {
            Name="slide",
            DurationSeconds=1f,
            Tracks=[
                new ImportedJointTrack { JointName="root",Translation=Line(1f,Vector3.Zero,new Vector3(2f,0f,0f)) },
                new ImportedJointTrack { JointName="child",Rotation=new ImportedKeys<Quaternion> { Times=[0f,1f],Values=[Quaternion.Identity,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2f)],Step=false } },
                new ImportedJointTrack { JointName="missing",Translation=Line(1f,Vector3.One,Vector3.One) },
            ],
        },Skeleton);
        Assert.Equal(2,Clip.BoundTrackCount);
        var Pose=new JointPose[2];
        Clip.Sample(Skeleton,0.25f,false,Pose);
        Assert.Equal(0.5f,Pose[0].Translation.X,4);
        Assert.Equal(Vector3.UnitY,Pose[1].Translation);
        Clip.Sample(Skeleton,0.5f,false,Pose);
        var Expected=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/4f);
        Assert.InRange(MathF.Abs(Quaternion.Dot(Expected,Pose[1].Rotation)),0.9999f,1.0001f);
        Clip.Sample(Skeleton,1.25f,true,Pose);
        Assert.Equal(0.5f,Pose[0].Translation.X,4);
        Clip.Sample(Skeleton,1.25f,false,Pose);
        Assert.Equal(2f,Pose[0].Translation.X,4);
        var Stepped=AnimationClip.Bind(new ImportedAnimationClip { Name="step",DurationSeconds=1f,Tracks=[new ImportedJointTrack { JointName="root",Translation=Line(1f,Vector3.Zero,new Vector3(2f,0f,0f),Step:true) }] },Skeleton);
        Stepped.Sample(Skeleton,0.9f,false,Pose);
        Assert.Equal(0f,Pose[0].Translation.X);
    }
}
