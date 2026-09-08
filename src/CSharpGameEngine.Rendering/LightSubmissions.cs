using System.Numerics;
using CSharpGameEngine.Lighting;
namespace CSharpGameEngine.Rendering;
internal static class LightSubmissionValidation {
    internal static bool Finite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
    internal static bool Direction(Vector3 Value)=>Finite(Value)&&float.IsFinite(Value.LengthSquared())&&Value.LengthSquared()>1e-12f;
    internal static bool Basis(Vector3 Forward,Vector3 Up)=>Direction(Forward)&&Direction(Up)&&Direction(Vector3.Cross(Vector3.Normalize(Up),Vector3.Normalize(Forward)));
    internal static bool Shadow(LightShadowSettings? Value)=>!Value.HasValue||Value.Value.IsValid;
}
public readonly record struct DirectionalLightSubmission(Vector3 Forward,LinearLightColor Color,Illuminance Illuminance,LightShadowSettings? Shadow=null) {
    public bool IsValid=>LightSubmissionValidation.Direction(this.Forward)&&this.Color.IsValid&&this.Illuminance.IsValid&&LightSubmissionValidation.Shadow(this.Shadow);
    public void Validate(){if(!this.IsValid){throw new ArgumentException("Invalid DirectionalLightSubmission values.");}}
}
public readonly record struct PointLightSubmission(Vector3 Position,LinearLightColor Color,LuminousFlux Flux,LightRange Range,LightShadowSettings? Shadow=null) {
    public bool IsValid=>LightSubmissionValidation.Finite(this.Position)&&this.Color.IsValid&&this.Flux.IsValid&&this.Range.IsValid&&LightSubmissionValidation.Shadow(this.Shadow);
    public void Validate(){if(!this.IsValid){throw new ArgumentException("Invalid PointLightSubmission values.");}}
}
public readonly record struct SpotLightSubmission(Vector3 Position,Vector3 Forward,LinearLightColor Color,LuminousFlux Flux,LightRange Range,SpotConeAngles Cone,LightShadowSettings? Shadow=null) {
    public bool IsValid=>LightSubmissionValidation.Finite(this.Position)&&LightSubmissionValidation.Direction(this.Forward)&&this.Color.IsValid&&this.Flux.IsValid&&this.Range.IsValid&&this.Cone.IsValid&&LightSubmissionValidation.Shadow(this.Shadow);
    public void Validate(){if(!this.IsValid){throw new ArgumentException("Invalid SpotLightSubmission values.");}}
}
public readonly record struct RectAreaLightSubmission(Vector3 Position,Vector3 Forward,Vector3 Up,LinearLightColor Color,Luminance Luminance,RectangleSize Size,LightRange Range,LightShadowSettings? Shadow=null) {
    public bool IsValid=>LightSubmissionValidation.Finite(this.Position)&&LightSubmissionValidation.Basis(this.Forward,this.Up)&&this.Color.IsValid&&this.Luminance.IsValid&&this.Range.IsValid&&this.Size.IsValid&&LightSubmissionValidation.Shadow(this.Shadow);
    public void Validate(){if(!this.IsValid){throw new ArgumentException("Invalid RectAreaLightSubmission values.");}}
}
