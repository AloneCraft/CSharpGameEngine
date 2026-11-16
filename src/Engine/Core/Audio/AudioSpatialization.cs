using System.Numerics;

namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// ランタイム音声システムが使う、バックエンド非依存の距離減衰。ECSでは音源とリスナーの位置をデータとして保持し、
/// この純粋関数が距離をスカラー音量に変換してから選択したバックエンドへ渡す。有限の最大距離を設けることで、
/// 非アクティブな音源が広大なワールド全体に音を漏らすのを防ぎ、ハードウェアなしでも決定的でテストしやすくする。
/// </summary>
public static class AudioSpatialization {
    public static float ComputeAttenuatedVolume(
        float SourceVolume,
        in Vector3 ListenerPosition,
        in Vector3 SourcePosition,
        float MinDistance,
        float MaxDistance){
        if(float.IsNaN(SourceVolume)||float.IsInfinity(SourceVolume)){
            return 0f;
        }

        MinDistance=MathF.Max(0f,MinDistance);
        MaxDistance=MathF.Max(MinDistance+float.Epsilon,MaxDistance);
        SourceVolume=Math.Clamp(SourceVolume,0f,1f);

        var Distance=Vector3.Distance(ListenerPosition,SourcePosition);
        if(Distance<=MinDistance){
            return SourceVolume;
        }

        if(Distance>=MaxDistance){
            return 0f;
        }

        var Normalized=(Distance-MinDistance)/(MaxDistance-MinDistance);
        // 滑らかな減衰により、どちらの境界でも音量が急に変化しない。
        var Fade=1f-(Normalized*Normalized*(3f-(2f*Normalized)));
        return SourceVolume*Math.Clamp(Fade,0f,1f);
    }
}
