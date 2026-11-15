using CSharpGameEngine.Lighting;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>インポートした発光。姿勢は所有するシーンノードが持つ。</summary>
public abstract record ImportedLight(LinearLightColor Color);
public sealed record ImportedDirectionalLight(LinearLightColor Color,Illuminance Illuminance):ImportedLight(Color);
public sealed record ImportedPointLight(LinearLightColor Color,LuminousIntensity Intensity,LightRange? Range):ImportedLight(Color);
public sealed record ImportedSpotLight(LinearLightColor Color,LuminousIntensity Intensity,LightRange? Range,ImportedSpotCone Cone):ImportedLight(Color);
/// <summary>情報を失わずに保持したglTF円錐。作成したエンジンの円錐では正の内側角が必要だが、glTFでは内側角0が許される。</summary>
public readonly record struct ImportedSpotCone {
    public ImportedSpotCone(float InnerHalfAngleRadians,float OuterHalfAngleRadians){
        LightValueValidation.Require(LightValueValidation.NonNegative(InnerHalfAngleRadians),nameof(InnerHalfAngleRadians));
        LightValueValidation.Require(LightValueValidation.Positive(OuterHalfAngleRadians)&&OuterHalfAngleRadians<=LightPhotometry.Pi/2f&&InnerHalfAngleRadians<OuterHalfAngleRadians,nameof(OuterHalfAngleRadians));
        this.InnerHalfAngleRadians=InnerHalfAngleRadians;this.OuterHalfAngleRadians=OuterHalfAngleRadians;
    }
    public float InnerHalfAngleRadians{get;}
    public float OuterHalfAngleRadians{get;}
    public SpotConeAngles ToAuthoredCone()=>new(this.InnerHalfAngleRadians*360f/LightPhotometry.Pi,this.OuterHalfAngleRadians*360f/LightPhotometry.Pi);
}
