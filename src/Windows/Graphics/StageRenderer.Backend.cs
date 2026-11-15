using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Backend;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12;
public sealed unsafe partial class StageRenderer:IStageRenderBackend {
    IStageShaderArtifact IStageRenderBackend.LastKnownGoodShaderArtifact=>this.LastKnownGoodShaderArtifact;
    ulong IStageRenderBackend.CompletedFence=>this.CompletedFence;
    bool IStageRenderBackend.LumenActiveLastFrame=>this.LumenActiveLastFrame;
    void IStageRenderBackend.SetCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews)=>this.SetCameraPreviews(Previews);
    RenderCameraPreviewDiagnostics[]IStageRenderBackend.GetCameraPreviewDiagnostics()=>this.GetCameraPreviewDiagnostics();
}
