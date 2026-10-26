using System;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Lighting;
/// <summary>レンダリングとアセット作成に依存しない、物理ライト量の共通検証。</summary>
public static class LightValueValidation {
    public static bool Finite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
    public static bool NonNegative(float Value)=>Finite(Value)&&Value>=0f;
    public static bool Positive(float Value)=>Finite(Value)&&Value>0f;
    public static void Require(bool Valid,string Parameter){
        if(!Valid){throw new ArgumentOutOfRangeException(Parameter,"The physical light value is outside its finite valid range.");}
    }
}
/// <summary>ルーメン単位で検証済みのLuminousFlux。</summary>
public readonly struct LuminousFlux:IEquatable<LuminousFlux> {
    public LuminousFlux(float Lumens){
        LightValueValidation.Require(LightValueValidation.NonNegative(Lumens),nameof(Lumens));
        this.Lumens=Lumens;
    }
    public float Lumens{get;}
    public bool IsValid=>LightValueValidation.NonNegative(this.Lumens);
    public bool Equals(LuminousFlux Other)=>this.Lumens.Equals(Other.Lumens);
    public override bool Equals(object? Other)=>Other is LuminousFlux Value&&this.Equals(Value);
    public override int GetHashCode()=>this.Lumens.GetHashCode();
    public static bool operator ==(LuminousFlux Left,LuminousFlux Right)=>Left.Equals(Right);
    public static bool operator !=(LuminousFlux Left,LuminousFlux Right)=>!Left.Equals(Right);
}
/// <summary>ルクス単位で検証済みのIlluminance。</summary>
public readonly struct Illuminance:IEquatable<Illuminance> {
    public Illuminance(float Lux){
        LightValueValidation.Require(LightValueValidation.NonNegative(Lux),nameof(Lux));
        this.Lux=Lux;
    }
    public float Lux{get;}
    public bool IsValid=>LightValueValidation.NonNegative(this.Lux);
    public bool Equals(Illuminance Other)=>this.Lux.Equals(Other.Lux);
    public override bool Equals(object? Other)=>Other is Illuminance Value&&this.Equals(Value);
    public override int GetHashCode()=>this.Lux.GetHashCode();
    public static bool operator ==(Illuminance Left,Illuminance Right)=>Left.Equals(Right);
    public static bool operator !=(Illuminance Left,Illuminance Right)=>!Left.Equals(Right);
}
/// <summary>ニト単位で検証済みのLuminance。</summary>
public readonly struct Luminance:IEquatable<Luminance> {
    public Luminance(float Nits){
        LightValueValidation.Require(LightValueValidation.NonNegative(Nits),nameof(Nits));
        this.Nits=Nits;
    }
    public float Nits{get;}
    public bool IsValid=>LightValueValidation.NonNegative(this.Nits);
    public bool Equals(Luminance Other)=>this.Nits.Equals(Other.Nits);
    public override bool Equals(object? Other)=>Other is Luminance Value&&this.Equals(Value);
    public override int GetHashCode()=>this.Nits.GetHashCode();
    public static bool operator ==(Luminance Left,Luminance Right)=>Left.Equals(Right);
    public static bool operator !=(Luminance Left,Luminance Right)=>!Left.Equals(Right);
}
/// <summary>カンデラ単位で検証済みのLuminousIntensity。</summary>
public readonly struct LuminousIntensity:IEquatable<LuminousIntensity> {
    public LuminousIntensity(float Candela){
        LightValueValidation.Require(LightValueValidation.NonNegative(Candela),nameof(Candela));
        this.Candela=Candela;
    }
    public float Candela{get;}
    public bool IsValid=>LightValueValidation.NonNegative(this.Candela);
    public bool Equals(LuminousIntensity Other)=>this.Candela.Equals(Other.Candela);
    public override bool Equals(object? Other)=>Other is LuminousIntensity Value&&this.Equals(Value);
    public override int GetHashCode()=>this.Candela.GetHashCode();
    public static bool operator ==(LuminousIntensity Left,LuminousIntensity Right)=>Left.Equals(Right);
    public static bool operator !=(LuminousIntensity Left,LuminousIntensity Right)=>!Left.Equals(Right);
}
/// <summary>メートル単位で検証済みのLightRange。</summary>
public readonly struct LightRange:IEquatable<LightRange> {
    public LightRange(float Meters){
        LightValueValidation.Require(LightValueValidation.Positive(Meters),nameof(Meters));
        this.Meters=Meters;
    }
    public float Meters{get;}
    public bool IsValid=>LightValueValidation.Positive(this.Meters);
    public bool Equals(LightRange Other)=>this.Meters.Equals(Other.Meters);
    public override bool Equals(object? Other)=>Other is LightRange Value&&this.Equals(Value);
    public override int GetHashCode()=>this.Meters.GetHashCode();
    public static bool operator ==(LightRange Left,LightRange Right)=>Left.Equals(Right);
    public static bool operator !=(LightRange Left,LightRange Right)=>!Left.Equals(Right);
}
/// <summary>メートル単位で検証済みのShadowBias。</summary>
public readonly struct ShadowBias:IEquatable<ShadowBias> {
    public ShadowBias(float Meters){
        LightValueValidation.Require(LightValueValidation.NonNegative(Meters),nameof(Meters));
        this.Meters=Meters;
    }
    public float Meters{get;}
    public bool IsValid=>LightValueValidation.NonNegative(this.Meters);
    public bool Equals(ShadowBias Other)=>this.Meters.Equals(Other.Meters);
    public override bool Equals(object? Other)=>Other is ShadowBias Value&&this.Equals(Value);
    public override int GetHashCode()=>this.Meters.GetHashCode();
    public static bool operator ==(ShadowBias Left,ShadowBias Right)=>Left.Equals(Right);
    public static bool operator !=(ShadowBias Left,ShadowBias Right)=>!Left.Equals(Right);
}
public readonly struct LinearLightColor {
    public LinearLightColor(float R,float G,float B){
        LightValueValidation.Require(LightValueValidation.NonNegative(R),nameof(R));
        LightValueValidation.Require(LightValueValidation.NonNegative(G),nameof(G));
        LightValueValidation.Require(LightValueValidation.NonNegative(B),nameof(B));
        this.R=R;this.G=G;this.B=B;
    }
    public float R{get;}
    public float G{get;}
    public float B{get;}
    public bool IsValid=>LightValueValidation.NonNegative(this.R)&&LightValueValidation.NonNegative(this.G)&&LightValueValidation.NonNegative(this.B);
}
public readonly struct RectangleSize {
    public RectangleSize(float WidthMeters,float HeightMeters){
        LightValueValidation.Require(LightValueValidation.Positive(WidthMeters),nameof(WidthMeters));
        LightValueValidation.Require(LightValueValidation.Positive(HeightMeters),nameof(HeightMeters));
        this.WidthMeters=WidthMeters;this.HeightMeters=HeightMeters;
    }
    public float WidthMeters{get;}
    public float HeightMeters{get;}
    public bool IsValid=>LightValueValidation.Positive(this.WidthMeters)&&LightValueValidation.Positive(this.HeightMeters);
}
public readonly struct SpotConeAngles {
    public SpotConeAngles([Angle(AngleUnit.Degrees)]float InnerFullAngleDegrees,[Angle(AngleUnit.Degrees)]float OuterFullAngleDegrees){
        LightValueValidation.Require(LightValueValidation.Positive(OuterFullAngleDegrees)&&OuterFullAngleDegrees<=180f,nameof(OuterFullAngleDegrees));
        LightValueValidation.Require(LightValueValidation.Positive(InnerFullAngleDegrees)&&InnerFullAngleDegrees<=OuterFullAngleDegrees,nameof(InnerFullAngleDegrees));
        this.InnerFullAngleDegrees=InnerFullAngleDegrees;this.OuterFullAngleDegrees=OuterFullAngleDegrees;
    }
    public float InnerFullAngleDegrees{get;}
    public float OuterFullAngleDegrees{get;}
    public bool IsValid=>LightValueValidation.Positive(this.InnerFullAngleDegrees)&&LightValueValidation.Positive(this.OuterFullAngleDegrees)&&this.InnerFullAngleDegrees<=this.OuterFullAngleDegrees&&this.OuterFullAngleDegrees<=180f;
}
public readonly struct LightShadowSettings {
    public LightShadowSettings(int Priority,ShadowBias Bias){
        LightValueValidation.Require(Bias.IsValid,nameof(Bias));
        this.Priority=Priority;this.Bias=Bias;
    }
    public int Priority{get;}
    public ShadowBias Bias{get;}
    public bool IsValid=>this.Bias.IsValid;
}
