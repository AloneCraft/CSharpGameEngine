using System;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アニメーションの再生状態（クリップの長さ・現在時刻・再生中・ループ・速度）。時刻の進め方だけを持つ純粋なモデルで、評価はホストが行う。
/// 時刻は 0 からクリップの長さまでで、ループなら折り返し、ループしないなら終端で止まる。
/// </summary>
internal sealed class AssetAnimationTimeline {
    internal const float MinimumSpeed=0.05f;
    internal const float MaximumSpeed=8f;
    private float Duration;
    private float Time;
    private float SpeedValue=1f;
    /// <summary>クリップの長さ（秒）。0 ならクリップなし（バインド姿勢）。</summary>
    internal float DurationSeconds=>this.Duration;
    internal float TimeSeconds=>this.Time;
    internal bool Playing { get;private set;}
    internal bool Loop { get;set;}=true;
    internal float Speed {
        get=>this.SpeedValue;
        set=>this.SpeedValue=float.IsNaN(value)?1f:Math.Min(MaximumSpeed,Math.Max(MinimumSpeed,value));
    }
    internal bool HasClip=>this.Duration>0f;
    /// <summary>クリップを切り替える。時刻は先頭へ戻し、再生は止める。長さが 0 以下や非有限ならクリップなし。</summary>
    internal void SetClip(float DurationSeconds){
        this.Duration=float.IsNaN(DurationSeconds)||float.IsInfinity(DurationSeconds)||DurationSeconds<0f?0f:DurationSeconds;
        this.Time=0f;
        this.Playing=false;
    }
    /// <summary>指定の時刻へ移す（スクラブ）。範囲外は 0 から長さに収める。</summary>
    internal void Seek(float Seconds){
        if(float.IsNaN(Seconds)){ return; }
        this.Time=Math.Min(this.Duration,Math.Max(0f,Seconds));
    }
    internal void Play(){
        if(!this.HasClip){ return; }
        // 終端で止まっていたら先頭から再生し直す。
        if(!this.Loop&&this.Time>=this.Duration){ this.Time=0f; }
        this.Playing=true;
    }
    internal void Pause()=>this.Playing=false;
    internal void Toggle(){
        if(this.Playing){ this.Pause(); } else { this.Play(); }
    }
    /// <summary>経過時間（秒）だけ時刻を進める。再生中でなければ何もしない。時刻が変わったら true。</summary>
    internal bool Advance(float ElapsedSeconds){
        if(!this.Playing||!this.HasClip||float.IsNaN(ElapsedSeconds)||float.IsInfinity(ElapsedSeconds)||ElapsedSeconds<=0f){ return false; }
        var Next=this.Time+ElapsedSeconds*this.SpeedValue;
        if(Next>=this.Duration){
            if(this.Loop){
                Next%=this.Duration;
            } else {
                Next=this.Duration;
                this.Playing=false;
            }
        }
        var Changed=Next!=this.Time;
        this.Time=Next;
        return Changed;
    }
    /// <summary>「0.50 / 1.20 s」の形の表示。</summary>
    internal string Format()=>this.Time.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+" / "+this.Duration.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+" s";
}
