using Xunit;
using CSharpGameEngine.Numerics;
using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Graphics.D3D12.Tests {
/// <summary>
/// RenderServer を通さず StageRenderer を直接描く試験のための、RenderServer と同じ渡し方（M0120）。
/// <list type="bullet">
/// <item>単位 4 で L0 からカメラと行列の互換入口を消したため、ビューは L5 の <see cref="RenderViewPolicy"/> で作って渡す。</item>
/// <item>単位 5a で L0 の <c>RenderFrame</c> を計画の実行に置き換えたため、描画面の今の状態からフレームの計画を作って実行する。</item>
/// <item>単位 5b で光源の提出と計画を L0 から L4 へ移したため、描画面ごとに <see cref="StageLightSubmissions"/> を持ち、描く前に計画し、表示できたらフレームを閉じる。</item>
/// </list>
/// 製品の経路は RenderServer を通す。
/// </summary>
internal static class StageRendererViewExtensions {
    private static readonly ConditionalWeakTable<StageRenderer,StageLightSubmissions>LightsByRenderer=new();
    /// <summary>この描画面の光源の提出。</summary>
    public static StageLightSubmissions Lights(this StageRenderer Renderer)=>LightsByRenderer.GetValue(Renderer,static _=>new StageLightSubmissions());
    /// <summary>カメラのビュー。描画面の縦横比（影の比較表示では半分の幅）で解決し、比較表示の切り替えでも解決し直す。</summary>
    public static void SetCamera(this StageRenderer Renderer,in RenderCamera Camera,float AspectRatioOverride=0f)=>
        Renderer.SetViewSource(RenderViewPolicy.FromCameraSource(Camera,AspectRatioOverride));
    /// <summary>正射影の行列のビュー。</summary>
    public static void SetViewProjection(this StageRenderer Renderer,in Matrix4x4 ViewProjection)=>
        Renderer.SetViewProjection(ViewProjection,Vector3.Zero);
    /// <summary>正射影の行列のビュー。</summary>
    public static void SetViewProjection(this StageRenderer Renderer,in Matrix4x4 ViewProjection,in Vector3 CameraPosition)=>
        Renderer.SetView(RenderViewPolicy.FromOrthographic(ViewProjection,CameraPosition));
    /// <summary>対数深度で描く透視の行列のビュー。描画面全体の縦横比で解決する。</summary>
    public static void SetViewProjection(this StageRenderer Renderer,in Matrix4x4 ViewProjection,in Vector3 CameraPosition,in RadialViewParameters Radial){
        var (Width,Height)=Renderer.SurfaceSize;
        Renderer.SetView(RenderViewPolicy.FromPerspective(ViewProjection,CameraPosition,Radial,(float)Width/Height));
    }
    /// <summary>1 フレームを描く。フレームと光源を計画して実行し、表示できたら光源のフレームを閉じる。</summary>
    public static void RenderFrame(this StageRenderer Renderer,bool CaptureForVerification=false){
        var Lights=Renderer.Lights();
        var Plan=StagePipelinePlanner.Build(new StagePipelinePlanKey(Renderer.FrameInputs,CaptureForVerification));
        Lights.Plan(Renderer.LightPlanInputs);
        if(Renderer.Execute(Plan,Lights)){
            Lights.EndFrame();
        }
    }
    /// <summary>単一の方向光（面から光源へ向かう向き）。置き換えられるまで毎フレーム有効。</summary>
    public static void SetDirectionalLight(this StageRenderer Renderer,Vector3 DirectionTowardLight,Vector3 Color,float Intensity,bool CastsShadows=true,float ShadowBiasMeters=0.0015f,int ShadowPriority=0)=>
        Renderer.Lights().SetDirectionalLight(DirectionTowardLight,Color,Intensity,CastsShadows,ShadowBiasMeters,ShadowPriority);
    /// <summary>このフレームに方向光（面から光源へ向かう向き）を追加する。</summary>
    public static void AddDirectionalLightTowardLight(this StageRenderer Renderer,Vector3 DirectionTowardLight,Vector3 Color,float Intensity,bool CastsShadows=true,int ShadowPriority=0,float ShadowBiasMeters=0.0015f)=>
        Renderer.Lights().AddDirectionalLightTowardLight(DirectionTowardLight,Color,Intensity,CastsShadows,ShadowPriority,ShadowBiasMeters);
    /// <summary>このフレームに点光源（光度）を追加する。</summary>
    public static void AddPointLight(this StageRenderer Renderer,Vector3 Position,Vector3 Color,float Intensity,float Range)=>
        Renderer.Lights().AddPointLight(Position,Color,Intensity,Range);
    /// <summary>このフレームに選択の優先度つきの点光源（光度）を追加する。</summary>
    public static void AddPointLight(this StageRenderer Renderer,Vector3 Position,Vector3 Color,float Intensity,float Range,int SelectionPriority)=>
        Renderer.Lights().AddPointLight(Position,Color,Intensity,Range,SelectionPriority);
    /// <summary>このフレームに影の設定つきの点光源（光度）を追加する。</summary>
    public static void AddPointLight(this StageRenderer Renderer,Vector3 Position,Vector3 Color,float Intensity,float Range,int SelectionPriority,bool CastsShadows,float ShadowBiasMeters)=>
        Renderer.Lights().AddPointLight(Position,Color,Intensity,Range,SelectionPriority,CastsShadows,ShadowBiasMeters);
    /// <summary>このフレームにスポットライト（光度、半角ラジアン）を追加する。</summary>
    public static void AddSpotLight(this StageRenderer Renderer,Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,float InnerConeAngleRadians,float OuterConeAngleRadians)=>
        Renderer.Lights().AddSpotLight(Position,Direction,Color,Intensity,Range,InnerConeAngleRadians,OuterConeAngleRadians);
    /// <summary>このフレームに選択の優先度つきのスポットライトを追加する。</summary>
    public static void AddSpotLight(this StageRenderer Renderer,Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,float InnerConeAngleRadians,float OuterConeAngleRadians,int SelectionPriority)=>
        Renderer.Lights().AddSpotLight(Position,Direction,Color,Intensity,Range,InnerConeAngleRadians,OuterConeAngleRadians,SelectionPriority);
    /// <summary>このフレームに影の設定つきのスポットライトを追加する。</summary>
    public static void AddSpotLight(this StageRenderer Renderer,Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,float InnerConeAngleRadians,float OuterConeAngleRadians,int SelectionPriority,bool CastsShadows,float ShadowBiasMeters)=>
        Renderer.Lights().AddSpotLight(Position,Direction,Color,Intensity,Range,InnerConeAngleRadians,OuterConeAngleRadians,SelectionPriority,CastsShadows,ShadowBiasMeters);
    /// <summary>このフレームに基底を指定した片面の矩形の光源を追加する。</summary>
    public static void AddAreaLight(this StageRenderer Renderer,Vector3 Position,Vector3 Direction,Vector3 Right,Vector3 Up,Vector3 LinearRgb,float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,int SelectionPriority,bool CastsShadows,float ShadowBiasMeters,PhysicalLightKind ShadowKind)=>
        Renderer.Lights().AddAreaLight(Position,Direction,Right,Up,LinearRgb,LuminanceNits,WidthMeters,HeightMeters,RangeMeters,SelectionPriority,CastsShadows,ShadowBiasMeters,ShadowKind);
    /// <summary>型付きの単一の方向光。</summary>
    public static void SetLight(this StageRenderer Renderer,in DirectionalLightSubmission Value)=>Renderer.Lights().SetLight(Value);
    /// <summary>型付きの方向光の追加。</summary>
    public static void Submit(this StageRenderer Renderer,in DirectionalLightSubmission Value)=>Renderer.Lights().Submit(Value);
    /// <summary>型付きの点光源の追加。</summary>
    public static void Submit(this StageRenderer Renderer,in PointLightSubmission Value)=>Renderer.Lights().Submit(Value);
    /// <summary>型付きのスポットライトの追加。</summary>
    public static void Submit(this StageRenderer Renderer,in SpotLightSubmission Value)=>Renderer.Lights().Submit(Value);
    /// <summary>型付きの矩形の光源の追加。</summary>
    public static void Submit(this StageRenderer Renderer,in RectAreaLightSubmission Value)=>Renderer.Lights().Submit(Value);
}
}
namespace CSharpGameEngine.Graphics.D3D12 {
    /// <summary>
    /// 物理単位の光源の提出（もとは L0 の <c>StagePhysicalLightApi</c> と <c>EmissivePatchSubmission</c>）。同じ名前の従来の入口より優先度が低くなるよう、
    /// もとと同じく外側の名前空間に置く（名前つきの引数で両方に当てはまる呼び出しは、従来の入口を選ぶ）。
    /// </summary>
    internal static class StageRendererPhysicalLightExtensions {
        /// <summary>物理単位の単一の方向光（光が進む向き、照度 lux）。</summary>
        public static void SetDirectionalLight(this StageRenderer Renderer,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            CSharpGameEngine.Graphics.D3D12.Tests.StageRendererViewExtensions.Lights(Renderer).SetPhysicalDirectionalLight(LightForward,LinearRgb,IlluminanceLux,CastsShadows,ShadowPriority,ShadowBiasMeters);
        /// <summary>このフレームに物理単位の方向光（光が進む向き、照度 lux）を追加する。</summary>
        public static void AddDirectionalLight(this StageRenderer Renderer,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            CSharpGameEngine.Graphics.D3D12.Tests.StageRendererViewExtensions.Lights(Renderer).AddPhysicalDirectionalLight(LightForward,LinearRgb,IlluminanceLux,CastsShadows,ShadowPriority,ShadowBiasMeters);
        /// <summary>このフレームに物理単位の点光源（光束 lm）を追加する。</summary>
        public static void AddPointLight(this StageRenderer Renderer,Vector3 Position,Vector3 LinearRgb,float LuminousFluxLumens,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            CSharpGameEngine.Graphics.D3D12.Tests.StageRendererViewExtensions.Lights(Renderer).AddPhysicalPointLight(Position,LinearRgb,LuminousFluxLumens,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
        /// <summary>このフレームに物理単位のスポットライト（光束 lm、全角の度）を追加する。</summary>
        public static void AddSpotLight(this StageRenderer Renderer,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,float LuminousFluxLumens,float RangeMeters,float InnerConeFullAngleDegrees,float OuterConeFullAngleDegrees,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            CSharpGameEngine.Graphics.D3D12.Tests.StageRendererViewExtensions.Lights(Renderer).AddPhysicalSpotLight(Position,LightForward,LinearRgb,LuminousFluxLumens,RangeMeters,InnerConeFullAngleDegrees,OuterConeFullAngleDegrees,CastsShadows,ShadowPriority,ShadowBiasMeters);
        /// <summary>このフレームに物理単位の片面の矩形の光源（輝度 nit）を追加する。</summary>
        public static void AddAreaLight(this StageRenderer Renderer,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            CSharpGameEngine.Graphics.D3D12.Tests.StageRendererViewExtensions.Lights(Renderer).AddPhysicalAreaLight(Position,LightForward,LinearRgb,LuminanceNits,WidthMeters,HeightMeters,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
        /// <summary>発光面のパッチを片面の矩形の光源として提出する。</summary>
        public static void SubmitDirectLighting(this StageRenderer Renderer,IReadOnlyList<EmissivePatch>Patches,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            CSharpGameEngine.Graphics.D3D12.Tests.StageRendererViewExtensions.Lights(Renderer).SubmitDirectLighting(Patches,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
    }
}
