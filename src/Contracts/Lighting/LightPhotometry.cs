using System;
namespace CSharpGameEngine.Lighting;
/// <summary>エンジンの二乗型スムーススポット減衰に使う、明示的な測光変換。</summary>
public static class LightPhotometry {
    public const float Pi=3.14159265358979323846f;
    public static LuminousIntensity PointIntensity(LuminousFlux Flux)=>new LuminousIntensity(Flux.Lumens/(4f*Pi));
    public static LuminousFlux PointFlux(LuminousIntensity Intensity)=>new LuminousFlux(Intensity.Candela*(4f*Pi));
    public static float SpotSolidAngle(SpotConeAngles Cone){
        LightValueValidation.Require(Cone.IsValid,nameof(Cone));
        var InnerCos=Cos(Cone.InnerFullAngleDegrees*Pi/360f);
        var OuterCos=Cos(Cone.OuterFullAngleDegrees*Pi/360f);
        var Result=2f*Pi*((1f-InnerCos)+(InnerCos-OuterCos)/3f);
        LightValueValidation.Require(LightValueValidation.Positive(Result),nameof(Cone));
        return Result;
    }
    public static LuminousIntensity SpotIntensity(LuminousFlux Flux,SpotConeAngles Cone)=>new LuminousIntensity(Flux.Lumens/SpotSolidAngle(Cone));
    public static LuminousFlux SpotFlux(LuminousIntensity Intensity,SpotConeAngles Cone)=>new LuminousFlux(Intensity.Candela*SpotSolidAngle(Cone));
    public static LuminousFlux RectangleFlux(Luminance Luminance,RectangleSize Size){
        LightValueValidation.Require(Size.IsValid,nameof(Size));
        return new LuminousFlux(Pi*Luminance.Nits*Size.WidthMeters*Size.HeightMeters);
    }
    public static Luminance RectangleLuminance(LuminousFlux Flux,RectangleSize Size){
        LightValueValidation.Require(Size.IsValid,nameof(Size));
        return new Luminance(Flux.Lumens/(Pi*Size.WidthMeters*Size.HeightMeters));
    }
    // netstandard2.0にはMathFがないため、BCLのdouble境界をこのアダプターに限定する。
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
    private static float Cos(float Radians)=>(float)Math.Cos(Radians);
}
