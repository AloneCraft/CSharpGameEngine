using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// Public physical-light submission API. It translates authored natural units into the existing
/// renderer's photometric shader inputs while the renderer's clustered GPU storage is evolved.
/// Directions are always Transform local +Z: the direction light travels, never a legacy
/// surface-to-light vector.
/// </summary>
public static class StagePhysicalLightApi {
    private const float MinimumDirectionLengthSquared=1e-12f;

    public static void SetDirectionalLight(
        this StageRenderer Renderer,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        // The legacy renderer takes the inverse (surface-to-light) convention. Its shadow pass
        // currently remains directional-only; the explicit settings are validated here and are
        // consumed by the atlas path as it replaces that pass.
        Renderer.SetDirectionalLight(
            -NormalizeForward(LightForward),LinearRgb,IlluminanceLux,CastsShadows,ShadowBiasMeters,ShadowPriority);
    }

    /// <summary>
    /// Adds one physical directional light to the current frame. At most four are retained by the
    /// renderer; its single legacy shadow map follows the highest-priority shadow caster.
    /// </summary>
    public static void AddDirectionalLight(
        this StageRenderer Renderer,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        Renderer.AddDirectionalLightTowardLight(
            -NormalizeForward(LightForward),LinearRgb,IlluminanceLux,
            CastsShadows,ShadowPriority,ShadowBiasMeters);
    }

    public static void AddPointLight(
        this StageRenderer Renderer,Vector3 Position,Vector3 LinearRgb,float LuminousFluxLumens,
        float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        Renderer.AddPointLight(Position,LinearRgb,PhysicalLightUnits.PointLumensToCandela(LuminousFluxLumens),
            RangeMeters,ShadowPriority,CastsShadows,ShadowBiasMeters);
    }

    public static void AddSpotLight(
        this StageRenderer Renderer,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminousFluxLumens,float RangeMeters,float InnerConeFullAngleDegrees,float OuterConeFullAngleDegrees,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        PhysicalLightUnits.ValidateSpotAngles(InnerConeFullAngleDegrees,OuterConeFullAngleDegrees);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        Renderer.AddSpotLight(
            Position,NormalizeForward(LightForward),LinearRgb,
            PhysicalLightUnits.SpotLumensToCandela(
                LuminousFluxLumens,InnerConeFullAngleDegrees,OuterConeFullAngleDegrees),
            RangeMeters,
            PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(InnerConeFullAngleDegrees),
            PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(OuterConeFullAngleDegrees),ShadowPriority,CastsShadows,ShadowBiasMeters);
    }

    /// <summary>
    /// Submits a one-sided rectangle as four deterministic, hemisphere-limited samples. The four
    /// uniform samples preserve total Lambertian flux (π × nits × area) and follow the Entity's
    /// actual Transform; this is the compatibility path until the rectangle LTC path is selected.
    /// </summary>
    public static void AddAreaLight(
        this StageRenderer Renderer,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        _=PhysicalLightUnits.AreaNitsToNormalCandela(LuminanceNits,WidthMeters,HeightMeters);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        var Forward=NormalizeForward(LightForward);
        var Reference=MathF.Abs(Vector3.Dot(Forward,Vector3.UnitY))>0.99f?Vector3.UnitX:Vector3.UnitY;
        var Right=Vector3.Normalize(Vector3.Cross(Reference,Forward));
        var Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        Renderer.AddAreaLight(
            Position,Forward,Right,Up,LinearRgb,LuminanceNits,WidthMeters,HeightMeters,RangeMeters,
            ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Area);
    }

    private static Vector3 NormalizeForward(Vector3 LightForward){
        if(!float.IsFinite(LightForward.X)||!float.IsFinite(LightForward.Y)||!float.IsFinite(LightForward.Z)||LightForward.LengthSquared()<=MinimumDirectionLengthSquared){
            throw new ArgumentOutOfRangeException(nameof(LightForward),"Light forward must be a finite non-zero vector.");
        }
        return Vector3.Normalize(LightForward);
    }

    private static void ValidateShadowSettings(int ShadowPriority,float ShadowBiasMeters){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters),"Shadow bias must be finite and non-negative meters.");
        }
    }
}
