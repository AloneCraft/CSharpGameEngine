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
        float sourceVolume,
        in Vector3 listenerPosition,
        in Vector3 sourcePosition,
        float minDistance,
        float maxDistance){
        if(float.IsNaN(sourceVolume)||float.IsInfinity(sourceVolume)){
            return 0f;
        }

        minDistance=MathF.Max(0f,minDistance);
        maxDistance=MathF.Max(minDistance+float.Epsilon,maxDistance);
        sourceVolume=Math.Clamp(sourceVolume,0f,1f);

        var distance=Vector3.Distance(listenerPosition,sourcePosition);
        if(distance<=minDistance){
            return sourceVolume;
        }

        if(distance>=maxDistance){
            return 0f;
        }

        var normalized=(distance-minDistance)/(maxDistance-minDistance);
        // Smooth falloff avoids an audible step at either boundary.
        var fade=1f-(normalized*normalized*(3f-(2f*normalized)));
        return sourceVolume*Math.Clamp(fade,0f,1f);
    }
}
