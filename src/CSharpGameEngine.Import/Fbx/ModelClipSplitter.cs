using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Import.Fbx;
internal static class ModelClipSplitter {
    internal static ModelAnimationClipData Split(ModelAnimationClipData Source,ModelClipRange Range){
        if(!(Source.SourceFramesPerSecond>0f)){ throw new InvalidDataException("MODEL_CLIP_RANGE: source frame rate missing."); }
        var Start=Range.StartFrame/Source.SourceFramesPerSecond;var End=Range.EndFrame/Source.SourceFramesPerSecond;
        if(Start<Source.StartSeconds||End>Source.EndSeconds+0.00001f||End<=Start){ throw new InvalidDataException("MODEL_CLIP_RANGE: frame range is outside the source take."); }
        ModelKeys<T>?Cut<T>(ModelKeys<T>?Keys,Func<T,T,float,T>Blend) where T:struct{
            if(Keys is null||Keys.Times.IsEmpty){ return null; }
            T Sample(float Time){
                if(Time<=Keys.Times[0]){ return Keys.Values[0]; }
                for(var I=1;I<Keys.Times.Length;I++){
                    if(Time<=Keys.Times[I]){ return Keys.Interpolation==ModelInterpolation.Step?Keys.Values[I-1]:Blend(Keys.Values[I-1],Keys.Values[I],(Time-Keys.Times[I-1])/(Keys.Times[I]-Keys.Times[I-1])); }
                }
                return Keys.Values[^1];
            }
            var Times=new[]{Start}.Concat(Keys.Times.Where(T=>T>Start&&T<End)).Append(End).ToArray();
            return new([..Times.Select(T=>T-Start)],[..Times.Select(Sample)],Keys.Interpolation,[],[]);
        }
        return Source with{Name=Range.Name.Normalize(),StartSeconds=0f,EndSeconds=End-Start,
            Tracks=[..Source.Tracks.Select(T=>T with{Translation=Cut(T.Translation,Vector3.Lerp),Rotation=Cut(T.Rotation,Quaternion.Slerp),Scale=Cut(T.Scale,Vector3.Lerp)})]};
    }
}
