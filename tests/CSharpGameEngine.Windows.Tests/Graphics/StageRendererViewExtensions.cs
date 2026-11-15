using System.Numerics;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// RenderServer を通さず StageRenderer を直接描く試験のための、ビューの渡し方。M0120 単位 4 で L0 からカメラと行列の互換入口を消したため、
/// 同じ値を L5 の <see cref="RenderViewPolicy"/> で作って渡す。製品の経路は RenderServer の <c>SetCamera</c>（L5 の拡張）を使う。
/// </summary>
internal static class StageRendererViewExtensions {
    /// <summary>カメラのビュー。描画面の縦横比（影の比較表示では半分の幅）で解決し、比較表示の切り替えでも解決し直す。</summary>
    public static void SetCamera(this StageRenderer Renderer,in RenderCamera Camera,float AspectRatioOverride=0f)=>
        Renderer.SetViewSource(RenderViewPolicy.FromCameraSource(Camera,AspectRatioOverride));
    /// <summary>正射影の行列のビュー。</summary>
    public static void SetViewProjection(this StageRenderer Renderer,in Matrix4x4 ViewProjection)=>
        Renderer.SetViewProjection(ViewProjection,Vector3.Zero);
    /// <summary>正射影の行列のビュー。</summary>
    public static void SetViewProjection(this StageRenderer Renderer,in Matrix4x4 ViewProjection,in Vector3 CameraPosition)=>
        Renderer.SetView(RenderViewPolicy.FromOrthographic(ViewProjection,CameraPosition));
    /// <summary>
    /// 1 フレームを描く。M0120 単位 5a で L0 の <c>RenderFrame</c> を L4 の計画の実行（<see cref="StageRenderer.Execute"/>）に置き換えたため、
    /// RenderServer と同じく、描画面の今の状態から計画を作って実行する。
    /// </summary>
    public static void RenderFrame(this StageRenderer Renderer,bool CaptureForVerification=false)=>
        Renderer.Execute(StagePipelinePlanner.Build(new StagePipelinePlanKey(Renderer.FrameInputs,CaptureForVerification)));
    /// <summary>対数深度で描く透視の行列のビュー。描画面全体の縦横比で解決する。</summary>
    public static void SetViewProjection(this StageRenderer Renderer,in Matrix4x4 ViewProjection,in Vector3 CameraPosition,in RadialViewParameters Radial){
        var (Width,Height)=Renderer.SurfaceSize;
        Renderer.SetView(RenderViewPolicy.FromPerspective(ViewProjection,CameraPosition,Radial,(float)Width/Height));
    }
}
