using Xunit;
using System.Numerics;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Import.Tests;
/// <summary>凍結済みの移行前オラクルを読むテスト専用の変換。製品ランタイムには含めない。</summary>
internal static class LegacyAnimationOracleAdapter {
    internal static Skeleton Skeleton(ImportedSkin Skin){
        var Rest=Enumerable.Range(0,Skin.JointNames.Count).Select(Index=>new ModelLocalTransform(Skin.RestTranslations[Index],Skin.RestRotations[Index],Skin.RestScales[Index]));
        var Data=new ModelSkeletonData("oracle",[..Skin.JointNames],[..Skin.ParentIndices],[..Rest],[..Enumerable.Repeat((byte)1,Skin.JointNames.Count)],Skin.SkeletonTransform);
        var Binding=new ModelSkinData("oracle",0,[..Enumerable.Range(0,Skin.JointNames.Count)],[..Skin.InverseBindMatrices],Matrix4x4.Identity);
        return CSharpGameEngine.Runtime.Animation.Skeleton.FromModel(Data,Binding);
    }
    internal static AnimationClip Clip(ImportedAnimationClip Clip,Skeleton Skeleton){
        static ModelKeys<T>?Keys<T>(ImportedKeys<T>?Value)where T:struct=>Value is null?null:new([..Value.Times],[..Value.Values],Value.Step?ModelInterpolation.Step:ModelInterpolation.Linear,[],[]);
        var Data=new ModelAnimationClipData(Clip.Name,0f,Clip.DurationSeconds,0f,0,[..Clip.Tracks.Select(Value=>new ModelTransformTrack(Value.JointName,-1,Keys(Value.Translation),Keys(Value.Rotation),Keys(Value.Scale)))],[],[],[],null);
        return AnimationClip.Bind(Data,Skeleton);
    }
}
