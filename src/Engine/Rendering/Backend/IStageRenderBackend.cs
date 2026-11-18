using CSharpGameEngine.Rendering.Pipeline;
using System.Numerics;
namespace CSharpGameEngine.Rendering.Backend;
/// <summary>Stage の描画のポート（ADR-0087 §1-3）。L4 が作ったフレーム計画（<see cref="StagePipelinePlan"/>）を実行する。</summary>
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
    void SetAmbientLight(Vector3 Color);
    void SetFog(FogParams Fog);
    void SetEnvironmentLighting(in EnvironmentLightingSettings Settings);
    void SetCameraAltitude(float Meters);
    void SetRenderOriginAltitude(float Meters);
    void SetWorldGridAnchor(int MaterialIndex,Vector3 Anchor);
    /// <summary>仮想シャドウの格子を世界に固定する区画と、描画空間の原点の区画の中の位置（M0132）。変更するまで値を保持する。</summary>
    void SetVirtualShadowAnchor(VirtualShadowAnchor Anchor);
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
    /// <summary>計画を決める、この描画面の今の状態。</summary>
    StageFrameInputs FrameInputs { get; }
    /// <summary>光源の計画に要る、描画面の今のビュー。</summary>
    StageLightPlanInputs LightPlanInputs { get; }
    /// <summary>
    /// 計画のパスを順に記録して表示する。<paramref name="Lights"/> は計画済みの光源で、実行器は結果を書くだけ。
    /// 検証用の読み戻しは計画のキーで決まる。描画面の大きさが 0 で表示しなかったら false（呼び出し側はフレームを閉じない）。
    /// </summary>
    bool Execute(StagePipelinePlan Plan,StageLightSubmissions Lights);
}
