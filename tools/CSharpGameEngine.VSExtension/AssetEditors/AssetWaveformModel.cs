using System;
using System.Globalization;
namespace CSharpGameEngine.VSExtension;
/// <summary>Sound Wave Editor の波形表示に使う純粋計算。</summary>
internal static class AssetWaveform {
    /// <summary>
    /// 区間ピークを表示列数へ再配分する。ピークが列より多い場合は列ごとに最大値をとり、少ない場合は同じピークを複数列に使う。
    /// 列数が 0 以下、またはピークがなければ空配列。
    /// </summary>
    internal static float[]Resample(float[]Peaks,int Columns){
        if(Peaks is null){ throw new ArgumentNullException(nameof(Peaks)); }
        if(Columns<=0||Peaks.Length==0){ return []; }
        var Result=new float[Columns];
        for(var Column=0;Column<Columns;Column++){
            var First=(int)((long)Column*Peaks.Length/Columns);
            var Last=Math.Max(First+1,(int)((long)(Column+1)*Peaks.Length/Columns));
            var Peak=0f;
            for(var Index=First;Index<Last&&Index<Peaks.Length;Index++){ Peak=Math.Max(Peak,Peaks[Index]); }
            Result[Column]=Peak;
        }
        return Result;
    }
    /// <summary>再生位置の割合（0..1）。長さが 0 以下なら 0。</summary>
    internal static float Fraction(float PositionSeconds,float DurationSeconds)=>
        DurationSeconds>0f?Math.Min(1f,Math.Max(0f,PositionSeconds/DurationSeconds)):0f;
    /// <summary>クリック位置（0..幅）から再生位置（秒）。</summary>
    internal static float SeekSeconds(float X,float Width,float DurationSeconds)=>
        Width>0f?Fraction(X,Width)*DurationSeconds:0f;
    /// <summary>m:ss.fff 形式。負数と非有限値は 0 として扱う。</summary>
    internal static string FormatTime(float Seconds){
        var Safe=!float.IsNaN(Seconds)&&!float.IsInfinity(Seconds)&&Seconds>0f?Seconds:0f;
        var Milliseconds=(long)(Safe*1000f+0.5f);
        var Minutes=Milliseconds/60000;
        var Rest=Milliseconds%60000;
        return string.Format(CultureInfo.InvariantCulture,"{0}:{1:00}.{2:000}",Minutes,Rest/1000,Rest%1000);
    }
}
