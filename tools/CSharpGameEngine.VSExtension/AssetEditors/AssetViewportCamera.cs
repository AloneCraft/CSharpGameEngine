using System;
using System.Numerics;
namespace CSharpGameEngine.VSExtension;
/// <summary>ポインタ入力に対するビューポートの操作結果。キャプチャの取得・解放を呼び出し側（HwndHost）に伝える。</summary>
internal enum AssetViewportNavigationResult { None, CameraChanged, CaptureStarted, CaptureEnded }
/// <summary>
/// アセットエディターの 3D ビューポートのカメラ。UE5 のメッシュエディターと同じ操作:
/// 左ドラッグ／右ドラッグ=オービット、中ドラッグ／Shift+左ドラッグ=パン、Ctrl+左ドラッグ=ドリー、ホイール=ドリー。
/// 視点の数式は Scene View と共有する <see cref="SceneViewState"/>／<see cref="SceneViewCamera"/> を使い、
/// 二重実装しない。設定（反転）は VS の設定ストアに依存するため呼び出し側が代入する。
/// </summary>
internal sealed class AssetViewportCamera {
    /// <summary>ターゲットから視点へ向かう既定の方向（既存のモデルプレビューと同じ斜め上から）。</summary>
    internal static readonly Vector3 DefaultViewDirection=Vector3.Normalize(new Vector3(1f,0.75f,1f));
    private const float DragDollyFactorPerPixel=1.01f;
    private const float MinFocusDistance=0.01f;
    /// <summary>一度に反映するホイール目盛り・ドラッグ画素の上限（<c>FloatMath.Pow</c> が float 範囲外を例外にするため）。</summary>
    private const int MaximumNotches=64;
    private const float MaximumDragPixels=2000f;
    private readonly SceneViewState State=new();
    private PointerButton DragButton=PointerButton.None;
    private int LastX;
    private int LastY;
    internal Vector3 BoundsCenter { get;private set;}
    internal float BoundsRadius { get;private set;}=1f;
    internal bool InvertOrbitX { get=>this.State.InvertOrbitX;set=>this.State.InvertOrbitX=value;}
    internal bool InvertOrbitY { get=>this.State.InvertOrbitY;set=>this.State.InvertOrbitY=value;}
    internal float Yaw=>this.State.Yaw;
    internal float Pitch=>this.State.Pitch;
    internal float FocusDistance=>this.State.FocusDistance;
    internal Vector3 FocusPosition=>this.State.FocusPosition;
    internal bool IsDragging=>this.DragButton!=PointerButton.None;
    /// <summary>
    /// 遠方クリップ面が届く距離を、境界球の半径の何倍にするか（既定 20）。床が見えるテンプレートでは、床の端が遠方クリップ面の内側に
    /// 見えて地平線とのあいだに何も描かれない帯ができないよう、床の広がりまで伸ばす。近クリップ面は遠方に比例するので、
    /// モデルの大きさに対する比率は変わらない。
    /// </summary>
    internal void SetFarReach(float Radii){
        this.FarReachRadii=Math.Max(DefaultFarReachRadii,!float.IsNaN(Radii)&&!float.IsInfinity(Radii)?Radii:DefaultFarReachRadii);
        this.ApplyFarReach();
    }
    private const float DefaultFarReachRadii=20f;
    private float FarReachRadii=DefaultFarReachRadii;
    // SceneViewState は LastKnownBoundsRadius の 20 倍を遠方の下限にする。
    private void ApplyFarReach()=>this.State.LastKnownBoundsRadius=this.BoundsRadius*this.FarReachRadii/DefaultFarReachRadii;
    /// <summary>境界球に合わせて視点を初期化する（既定の向きと視野角）。</summary>
    internal void Frame(Vector3 Center,float Radius,float AspectRatio){
        this.BoundsCenter=Center;
        this.BoundsRadius=Radius>0f?Radius:1f;
        this.State.ProjectionMode=SceneProjectionMode.Perspective;
        this.State.ResetToBounds(Center,this.BoundsRadius,DefaultViewDirection,SceneCameraFraming.DefaultFovYRadians,Math.Max(AspectRatio,0.001f));
        this.ApplyFarReach();
    }
    /// <summary>境界球に合わせて、プレビューレベルのカメラ初期値（方向・視野角・投影方式）で視点を初期化する（フレーム操作と明示的なリセット）。</summary>
    internal void Frame(Vector3 Center,float Radius,float AspectRatio,PreviewCameraDefaults Defaults){
        this.BoundsCenter=Center;
        this.BoundsRadius=Radius>0f?Radius:1f;
        this.State.ProjectionMode=Defaults.Projection==PreviewProjection.Orthographic?SceneProjectionMode.Orthographic:SceneProjectionMode.Perspective;
        this.State.ResetToBounds(Center,this.BoundsRadius,ViewDirection(Defaults),Defaults.FovDegrees*FloatMath.Pi/180f,Math.Max(AspectRatio,0.001f));
        this.ApplyFarReach();
    }
    /// <summary>対象から視点へ向かう単位ベクトル（方位角・仰角から）。</summary>
    internal static Vector3 ViewDirection(PreviewCameraDefaults Defaults){
        var Yaw=(Defaults.YawDegrees%360f)*FloatMath.Pi/180f;
        var Elevation=Defaults.ElevationDegrees*FloatMath.Pi/180f;
        return Vector3.Normalize(new Vector3(FloatMath.Cos(Elevation)*FloatMath.Sin(Yaw),FloatMath.Sin(Elevation),FloatMath.Cos(Elevation)*FloatMath.Cos(Yaw)));
    }
    internal bool IsOrthographic=>this.State.ProjectionMode==SceneProjectionMode.Orthographic;
    internal void Orbit(float DeltaX,float DeltaY)=>this.State.ApplyOrbit(DeltaX,DeltaY);
    internal void Pan(float DeltaX,float DeltaY)=>this.State.ApplyPan(DeltaX,DeltaY);
    internal void Dolly(int WheelNotches){
        var Notches=Math.Max(-MaximumNotches,Math.Min(MaximumNotches,WheelNotches));
        if(this.IsOrthographic){ this.State.ApplyZoom(Notches); } else { this.State.ApplyDolly(Notches); }
    }
    /// <summary>ドラッグによるドリー。<paramref name="DeltaY"/> が正（下方向）で遠ざかる。</summary>
    internal void DollyByPixels(float DeltaY){
        var Factor=FloatMath.Pow(DragDollyFactorPerPixel,Math.Max(-MaximumDragPixels,Math.Min(MaximumDragPixels,DeltaY)));
        if(this.IsOrthographic){ this.State.OrthographicSize=Math.Max(this.State.OrthographicSize*Factor,MinFocusDistance); }
        else { this.State.FocusDistance=Math.Max(this.State.FocusDistance*Factor,MinFocusDistance); }
    }
    internal Matrix4x4 View=>this.State.GetView();
    internal Matrix4x4 Projection(float AspectRatio)=>this.State.GetProjection(AspectRatio);
    internal Vector3 Eye=>this.State.GetEyePosition();
    internal float FovYRadians=>this.State.FovYRadians;
    /// <summary>
    /// ポインタイベントを処理する。ドラッグ中は押下ボタンが離されるまでそのボタンの操作を続ける（別ボタンの押下は無視）。
    /// </summary>
    internal AssetViewportNavigationResult Handle(PointerEvent Event){
        switch(Event.Kind){
            case PointerEventKind.Down:
                if(this.DragButton!=PointerButton.None||Event.Button==PointerButton.None){ return AssetViewportNavigationResult.None; }
                this.DragButton=Event.Button;
                this.LastX=Event.X;
                this.LastY=Event.Y;
                return AssetViewportNavigationResult.CaptureStarted;
            case PointerEventKind.Move:
                if(this.DragButton==PointerButton.None){ return AssetViewportNavigationResult.None; }
                var DeltaX=Event.X-this.LastX;
                var DeltaY=Event.Y-this.LastY;
                this.LastX=Event.X;
                this.LastY=Event.Y;
                if(DeltaX==0&&DeltaY==0){ return AssetViewportNavigationResult.None; }
                this.ApplyDrag(this.DragButton,Event.Modifiers,DeltaX,DeltaY);
                return AssetViewportNavigationResult.CameraChanged;
            case PointerEventKind.Up:
                if(Event.Button!=this.DragButton){ return AssetViewportNavigationResult.None; }
                this.DragButton=PointerButton.None;
                return AssetViewportNavigationResult.CaptureEnded;
            case PointerEventKind.CaptureLost:
                if(this.DragButton==PointerButton.None){ return AssetViewportNavigationResult.None; }
                this.DragButton=PointerButton.None;
                return AssetViewportNavigationResult.CaptureEnded;
            default:
                return AssetViewportNavigationResult.None;
        }
    }
    private void ApplyDrag(PointerButton Button,RawInputModifiers Modifiers,int DeltaX,int DeltaY){
        if(Button==PointerButton.Middle||Button==PointerButton.Left&&Modifiers.Shift){ this.Pan(DeltaX,DeltaY);return; }
        if(Button==PointerButton.Left&&Modifiers.Ctrl){ this.DollyByPixels(DeltaY);return; }
        this.Orbit(DeltaX,DeltaY);
    }
}
