using System.Numerics;

namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// Backend-independent distance attenuation used by runtime audio systems. The ECS keeps source
/// and listener positions as data; this pure function turns them into a scalar volume before the
/// selected backend receives it. A finite maximum distance keeps an inactive source from leaking
/// sound across a large world while still being deterministic and easy to test without hardware.
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
        // Smooth falloff avoids an audible step at either boundary.
        var Fade=1f-(Normalized*Normalized*(3f-(2f*Normalized)));
        return SourceVolume*Math.Clamp(Fade,0f,1f);
    }
}
