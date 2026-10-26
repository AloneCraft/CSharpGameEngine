using System.Numerics;
namespace CSharpGameEngine.Rendering.Backend;
/// <summary>現在の Stage 送信境界。次の抽出段階では、順序付きネイティブフレーム記録を、明示的なフレーム計画の実行に置き換える。</summary>
public interface IStageRenderBackend:IDisposable {
    string AdapterDescription { get; }
    bool IsWarp { get; }
    bool DebugLayerEnabled { get; }
    bool GpuBasedValidationEnabled { get; }
    IReadOnlyList<RenderDeviceMessage>DebugMessages { get; }
    int ErrorOrWorseMessageCount { get; }
    bool ShaderArtifactLoaded { get; }
    string ShaderSemanticHash { get; }
    IReadOnlyList<string>ShaderArtifactDiagnostics { get; }
    uint FramesRendered { get; }
    ulong CompletedFence { get; }
    int ResidentMaterialTextureCount { get; }
    int ResidentScreenTextureCount { get; }
    PixelReadbackResult?LastCapturedFrame { get; }
    int FreeMaterialSlots { get; }
    int ActiveObjectCount { get; }
    int DistanceFieldMeshCount { get; }
    int DistanceFieldObjectCount { get; }
    VirtualShadowStatistics VirtualShadowStatistics { get; }
    DirectionalShadowSettings DirectionalShadows { get; }
    float CascadedShadowGpuMilliseconds { get; }
    float VirtualShadowGpuMilliseconds { get; }
    RenderScalability Scalability { get; set; }
    LumenVisualization LumenVisualization { get; set; }
    bool LumenActiveLastFrame { get; }
    IStageShaderArtifact LastKnownGoodShaderArtifact { get; }
    int AddMesh(StageMeshCreateInfo Mesh);
    int AddMaterial(StageMaterialCreateInfo Material);
    int AddObject(int MeshIndex,int MaterialIndex);
    void RemoveObject(int ObjectIndex);
    void SetView(in RenderView View);
    void SetLight(in DirectionalLightSubmission Value);
    void Submit(in DirectionalLightSubmission Value);
    void Submit(in PointLightSubmission Value);
    void Submit(in SpotLightSubmission Value);
    void Submit(in RectAreaLightSubmission Value);
    void SubmitEmissivePatch(in EmissivePatch Patch,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters);
    void SubmitDirectLighting(IReadOnlyList<EmissivePatch>Patches,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters);
    void SetAmbientLight(Vector3 Color);
    void SetFog(FogParams Fog);
    void SetEnvironmentLighting(in EnvironmentLightingSettings Settings);
    void SetCameraAltitude(float Meters);
    void SetRenderOriginAltitude(float Meters);
    void SetWorldGridAnchor(int MaterialIndex,Vector3 Anchor);
    void SetObjectTransform(int ObjectIndex,Matrix4x4 World);
    void SetSkinPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette);
    void SetPresentationMode(RenderPresentationMode Mode);
    void SetObjectVisibility(int ObjectIndex,bool VisibleInView,bool CastsShadow);
    void SetObjectEmissiveScale(int ObjectIndex,float Scale);
    void SetObjectSelectionWeight(int ObjectIndex,float Weight);
    void SetOverlay(ReadOnlySpan<RenderOverlayVertex>Triangles,ReadOnlySpan<RenderOverlayVertex>Lines);
    void SetScreenLayer(ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches);
    void SetCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews);
    RenderCameraPreviewDiagnostics[]GetCameraPreviewDiagnostics();
    void Resize(int Width,int Height);
    void RenderFrame(bool CaptureForVerification=false);
}
