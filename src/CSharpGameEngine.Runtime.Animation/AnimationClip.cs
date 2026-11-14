using System.Numerics;
using CSharpGameEngine.Content.Models;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// 関節名で <see cref="Skeleton"/> に結び付けたインポート済みアニメーションクリップ（ADR-0066）。サンプリングするとローカル姿勢全体を書き込む。クリップが動かさない関節は
/// スケルトンの静止時変換を保ち、スケルトンにない関節のトラックは無視する。
/// </summary>
public sealed class AnimationClip {
    private readonly record struct Track(ModelKeys<Vector3>?Translation,ModelKeys<Quaternion>?Rotation,ModelKeys<Vector3>?Scale);
    private readonly (int Joint,Track Track)[]Tracks;
    private readonly float StartSeconds;
    private AnimationClip(string Name,float Start,float Duration,(int Joint,Track Track)[]Tracks){
        this.Name=Name;
        this.DurationSeconds=Duration;
        this.StartSeconds=Start;
        this.Tracks=Tracks;
    }
    public string Name { get; }
    public float DurationSeconds { get; }
    public int BoundTrackCount=>this.Tracks.Length;
    public static AnimationClip Bind(ModelAnimationClipData Clip,Skeleton Skeleton){
        ArgumentNullException.ThrowIfNull(Clip);ArgumentNullException.ThrowIfNull(Skeleton);
        if(!float.IsFinite(Clip.StartSeconds)||!float.IsFinite(Clip.EndSeconds)||Clip.EndSeconds<Clip.StartSeconds){ throw new InvalidDataException("Animation clip time range is invalid."); }
        return new(Clip.Name,Clip.StartSeconds,Clip.EndSeconds-Clip.StartSeconds,[..Clip.Tracks.Select(Value=>(Skeleton.IndexOf(Value.JointName),new Track(Value.Translation,Value.Rotation,Value.Scale))).Where(static Pair=>Pair.Item1>=0)]);
    }
    /// <summary><paramref name="Loop"/> が有効なら <paramref name="Time"/> をクリップ内に折り返す。無効なら両端の範囲に収める。</summary>
    public float ClipTime(float Time,bool Loop){
        if(this.DurationSeconds<=0f||!float.IsFinite(Time)){
            return 0f;
        }
        if(!Loop){
            return Math.Clamp(Time,0f,this.DurationSeconds);
        }
        var Wrapped=Time%this.DurationSeconds;
        return Wrapped<0f?Wrapped+this.DurationSeconds:Wrapped;
    }
    public void Sample(Skeleton Skeleton,float Time,bool Loop,Span<JointPose>Pose){
        Skeleton.Rest.CopyTo(Pose);
        var Local=this.StartSeconds+this.ClipTime(Time,Loop);
        foreach(var (Joint,Track) in this.Tracks){
            if(Track.Translation is{ }Translation){
                Pose[Joint].Translation=SampleVector(Translation,Local);
            }
            if(Track.Rotation is{ }Rotation){
                Pose[Joint].Rotation=SampleRotation(Rotation,Local);
            }
            if(Track.Scale is{ }Scale){
                Pose[Joint].Scale=SampleVector(Scale,Local);
            }
        }
    }
    /// <summary><paramref name="Time"/> 以前または同時刻のキーと、次のキーまでの補間率（ステップキーと両端では0）。</summary>
    internal static (int Index,float Fraction)Locate<T>(ModelKeys<T>Keys,float Time)where T:struct{
        var Times=Keys.Times;
        if(Times.Length<=1||Time<=Times[0]){
            return (0,0f);
        }
        if(Time>=Times[^1]){
            return (Times.Length-1,0f);
        }
        var Low=0;
        var High=Times.Length-1;
        while(High-Low>1){
            var Middle=(Low+High)/2;
            if(Times[Middle]<=Time){
                Low=Middle;
            }
            else {
                High=Middle;
            }
        }
        if(Keys.Interpolation==ModelInterpolation.Step){
            return (Low,0f);
        }
        var Span=Times[High]-Times[Low];
        return (Low,Span>0f?(Time-Times[Low])/Span:0f);
    }
    private static Vector3 SampleVector(ModelKeys<Vector3>Keys,float Time){
        var (Index,Fraction)=Locate(Keys,Time);
        return Fraction<=0f?Keys.Values[Index]:Vector3.Lerp(Keys.Values[Index],Keys.Values[Index+1],Fraction);
    }
    private static Quaternion SampleRotation(ModelKeys<Quaternion>Keys,float Time){
        var (Index,Fraction)=Locate(Keys,Time);
        return Fraction<=0f?Keys.Values[Index]:Quaternion.Slerp(Keys.Values[Index],Keys.Values[Index+1],Fraction);
    }
}
