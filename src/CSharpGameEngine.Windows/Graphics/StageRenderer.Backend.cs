using PublicDirectionalLightSubmission=CSharpGameEngine.Rendering.DirectionalLightSubmission;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Backend;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12;
public sealed unsafe partial class StageRenderer:IStageRenderBackend {
    IStageShaderArtifact IStageRenderBackend.LastKnownGoodShaderArtifact=>this.LastKnownGoodShaderArtifact;
    ulong IStageRenderBackend.CompletedFence=>this.CompletedFence;
    bool IStageRenderBackend.LumenActiveLastFrame=>this.LumenActiveLastFrame;
    void IStageRenderBackend.SetLight(in PublicDirectionalLightSubmission Value)=>TypedLightBackend.SetLight(this,Value);
    void IStageRenderBackend.Submit(in PublicDirectionalLightSubmission Value)=>TypedLightBackend.Submit(this,Value);
    void IStageRenderBackend.Submit(in PointLightSubmission Value)=>TypedLightBackend.Submit(this,Value);
    void IStageRenderBackend.Submit(in SpotLightSubmission Value)=>TypedLightBackend.Submit(this,Value);
    void IStageRenderBackend.Submit(in RectAreaLightSubmission Value)=>TypedLightBackend.Submit(this,Value);
    void IStageRenderBackend.SubmitDirectLighting(IReadOnlyList<EmissivePatch>Patches,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
        EmissivePatchSubmission.SubmitDirectLighting(this,Patches,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
    void IStageRenderBackend.SubmitEmissivePatch(in EmissivePatch Patch,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
        this.AddAreaLight(Patch.Center,Patch.Forward,Patch.Right,Patch.Up,Patch.LinearRgb,Patch.LuminanceNits,Patch.WidthMeters,Patch.HeightMeters,
            RangeMeters,ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Emissive);
    void IStageRenderBackend.SetCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews)=>this.SetCameraPreviews(Previews);
    RenderCameraPreviewDiagnostics[]IStageRenderBackend.GetCameraPreviewDiagnostics()=>this.GetCameraPreviewDiagnostics();
}
