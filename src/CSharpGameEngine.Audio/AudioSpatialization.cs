using System.Numerics;

namespace CSharpGameEngine.Audio;

public readonly record struct AudioSpatialParameters {
    public AudioSpatialParameters(float MinimumDistance,float MaximumDistance){
        if(!float.IsFinite(MinimumDistance)||MinimumDistance<0f){
            throw new ArgumentOutOfRangeException(nameof(MinimumDistance));
        }
        if(!float.IsFinite(MaximumDistance)||MaximumDistance<=MinimumDistance){
            throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        }
        this.MinimumDistance=MinimumDistance;
        this.MaximumDistance=MaximumDistance;
    }
    public float MinimumDistance { get; }
    public float MaximumDistance { get; }
    public static AudioSpatialParameters Default=>new(1f,100f);
}

public readonly record struct AudioSpatialMix(float Attenuation,float Pan,float Distance);

/// <summary>
/// Converts an already listener-local float position into portable stereo mix parameters.
/// Planetary/frame composition remains fixed-point upstream; this boundary never accepts or
/// creates an absolute float world coordinate.
/// </summary>
public static class AudioSpatializer {
    public static bool TryCalculate(Vector3 ListenerLocalPosition,
        in AudioSpatialParameters Parameters,out AudioSpatialMix Mix){
        if(!float.IsFinite(ListenerLocalPosition.X)||!float.IsFinite(ListenerLocalPosition.Y)
            ||!float.IsFinite(ListenerLocalPosition.Z)
            ||Parameters.MaximumDistance<=Parameters.MinimumDistance
            ||Parameters.MinimumDistance<0f
            ||!float.IsFinite(Parameters.MinimumDistance)
            ||!float.IsFinite(Parameters.MaximumDistance)){
            Mix=default;
            return false;
        }
        float DistanceSquared=Vector3.Dot(ListenerLocalPosition,ListenerLocalPosition);
        if(!float.IsFinite(DistanceSquared)){
            Mix=default;
            return false;
        }
        float Distance=MathF.Sqrt(DistanceSquared);
        float Attenuation=Distance<=Parameters.MinimumDistance
            ?1f
            :Distance>=Parameters.MaximumDistance
                ?0f
                :1f-(Distance-Parameters.MinimumDistance)
                    /(Parameters.MaximumDistance-Parameters.MinimumDistance);
        float HorizontalSquared=ListenerLocalPosition.X*ListenerLocalPosition.X
            +ListenerLocalPosition.Z*ListenerLocalPosition.Z;
        float Pan=HorizontalSquared<=1e-12f
            ?0f
            :Math.Clamp(ListenerLocalPosition.X/MathF.Sqrt(HorizontalSquared),-1f,1f);
        Mix=new AudioSpatialMix(Attenuation,Pan,Distance);
        return true;
    }
}
