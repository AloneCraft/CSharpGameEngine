using System.Numerics;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
internal static class AnimationTestData {
    internal static Skeleton Create(string[]Names,int[]Parents,Vector3[]Translations,Matrix4x4[]?Inverse=null){
        var Data=new ModelSkeletonData("test",[..Names],[..Parents],[..Translations.Select(static Value=>new ModelLocalTransform(Value,Quaternion.Identity,Vector3.One))],
            [..Enumerable.Repeat((byte)1,Names.Length)],Matrix4x4.Identity);
        var Skin=new ModelSkinData("test",0,[..Enumerable.Range(0,Names.Length)],Inverse is null?[..Enumerable.Repeat(Matrix4x4.Identity,Names.Length)]:[..Inverse],Matrix4x4.Identity);
        return Skeleton.FromModel(Data,Skin);
    }
    internal static Skeleton TwoJoints(){
        var Rest=Matrix4x4.CreateTranslation(0f,1f,0f);
        if(!Matrix4x4.Invert(Rest,out var Inverse)){ throw new InvalidOperationException("Invalid two-joint test rest pose."); }
        return Create(["root","child"],[-1,0],[Vector3.Zero,Vector3.UnitY],[Matrix4x4.Identity,Inverse]);
    }
    internal static ModelKeys<Vector3>Line(float Duration,Vector3 From,Vector3 To,bool Step=false,float Start=0f)=>new([Start,Start+Duration],[From,To],Step?ModelInterpolation.Step:ModelInterpolation.Linear,[],[]);
    internal static ModelAnimationClipData Clip(string Name,float Duration,ModelTransformTrack[]Tracks,float Start=0f)=>new(Name,Start,Start+Duration,0f,0,[..Tracks],[],[],[],null);
    internal static AnimationClip Marker(Skeleton Skeleton,float Marker,float Duration)=>AnimationClip.Bind(Clip($"marker{Marker}",Duration,
        [new("child",1,Line(Duration,new(Marker,1f,0f),new(Marker,1f,0f)),null,null)]),Skeleton);
}
