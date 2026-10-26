using CSharpGameEngine.Rendering.Pipeline;
using System.Numerics;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12;
internal static class TypedLightBackend {
    internal static void Submit(this StageRenderer Renderer,in DirectionalLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        Renderer.AddDirectionalLight(Value.Forward,Color,Value.Illuminance.Lux,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    internal static void Submit(this StageRenderer Renderer,in PointLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        Renderer.AddPointLight(Value.Position,Color,Value.Flux.Lumens,Value.Range.Meters,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    internal static void Submit(this StageRenderer Renderer,in SpotLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        Renderer.AddSpotLight(Value.Position,Value.Forward,Color,Value.Flux.Lumens,Value.Range.Meters,Value.Cone.InnerFullAngleDegrees,Value.Cone.OuterFullAngleDegrees,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    internal static void Submit(this StageRenderer Renderer,in RectAreaLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        var Forward=Vector3.Normalize(Value.Forward);
        var Right=Vector3.Normalize(Vector3.Cross(Vector3.Normalize(Value.Up),Forward));
        var Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        Renderer.AddAreaLight(Value.Position,Forward,Right,Up,Color,Value.Luminance.Nits,Value.Size.WidthMeters,Value.Size.HeightMeters,Value.Range.Meters,Shadow?.Priority??0,Shadow.HasValue,Shadow?.Bias.Meters??0f,PhysicalLightKind.Area);
    }
    internal static void SetLight(this StageRenderer Renderer,in DirectionalLightSubmission Value){
        Value.Validate();
        var Shadow=Value.Shadow;
        Renderer.SetDirectionalLight(Value.Forward,new Vector3(Value.Color.R,Value.Color.G,Value.Color.B),Value.Illuminance.Lux,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
}
