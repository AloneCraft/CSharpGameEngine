using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>仕様書§3の永続ツール状態。ギズモは未実装(M0020 Phase 3)だが値は先に確定させる。</summary>
internal enum SceneActiveTool { Select, Move, Rotate, Scale }

internal enum SceneTransformSpace { World, Local }

internal enum SceneProjectionMode { Perspective, Orthographic }

/// <summary>Unity/Unreal/Blender/Mayaの具体値充填はADR-0041決定事項7によりスコープ外。器のみ用意する。</summary>
internal enum SceneNavigationPreset { Default, Custom }

/// <summary>Fly(RMB+WASDQE)で押下追跡する移動キー。<c>SceneInputStateMachine</c>が保持集合を管理する。</summary>
internal enum NavigationKey { W, A, S, D, Q, E }

/// <summary>
/// Scene View の編集ビュー状態。所有者は<c>SceneHwndHost</c> — <c>SceneRenderer</c>は再解析のたびに
/// 破棄・再生成されるため、視点とツール状態はここに置く(ADR-0041 決定事項1)。
///
/// <para>
/// 永続する7項目(<see cref="ActiveTool"/>/<see cref="TransformSpace"/>/<see cref="ProjectionMode"/>/
/// <see cref="NavigationPreset"/>/<see cref="NavigationSpeedMultiplier"/>/<see cref="InvertLookX"/>/
/// <see cref="InvertLookY"/>)は<see cref="SceneEditorSettings"/>経由でVS再起動を跨いで残る
/// (Docs/Specifications/Scene-Editor-Design.md §10)。それ以外の視点フィールドはセッション内のみ
/// (仕様§20: SceneViewはSceneDeclarationへ保存しない)。
/// </para>
///
/// <para>
/// <c>FollowView</c>/<c>Duplicate</c>/<c>VertexSnap</c>/<c>SurfaceSnap</c>/<c>FastNavigation</c>/
/// <c>PrecisionNavigation</c>は仕様§3が「永続モードにしない」と明示しているため、このクラスの
/// フィールドにはしない — Shift/Ctrlの押下状態は呼び出し側(<c>SceneInputStateMachine</c>)が
/// 一時値として渡す。
/// </para>
/// </summary>
internal sealed class SceneViewState {
    // 視点(セッション内のみ)。
    internal Vector3 FocusPosition;
    internal float FocusDistance=1f;
    internal float Yaw;
    internal float Pitch;
    internal float FovYRadians=SceneCameraFraming.DefaultFovYRadians;
    internal float OrthographicSize=5f;

    /// <summary>Far平面算出用。ビルド成功のたびにSceneHwndHostが更新する(セッション内のみ)。</summary>
    internal float LastKnownBoundsRadius=1f;

    // 永続ツール状態(7項目)。
    internal SceneActiveTool ActiveTool;
    internal SceneTransformSpace TransformSpace;
    internal SceneProjectionMode ProjectionMode;
    internal SceneNavigationPreset NavigationPreset;
    internal float NavigationSpeedMultiplier=1f;
    internal bool InvertLookX;
    internal bool InvertLookY;

    private const float NearPlane=0.05f;
    private const float FarDistanceFactor=20f;

    // SceneCameraFramingと同値のフロア(Sky dome保護、Phase 0踏襲)。
    private const float FarPlaneRadiusFactor=20f;
    private const float MinFocusDistance=0.01f;
    private const float MinNavigationSpeedMultiplier=0.05f;
    private const float MaxNavigationSpeedMultiplier=20f;

    // 仕様§3どおり pow(0.9, notch) / pow(1.2, notch)。
    private const float DollyNotchFactor=0.9f;
    private const float SpeedAdjustNotchFactor=1.2f;

    // ADR-0041 §4.3。
    private const float FastNavigationMultiplier=4f;
    private const float PrecisionNavigationMultiplier=0.25f;

    private const float FlySpeedPerFocusDistance=1f;
    private const float LookSensitivity=0.0025f;
    private const float OrbitSensitivity=0.0025f;
    private const float PanSensitivityPerFocusDistance=0.0025f;

    /// <summary>呼び出しはHwndHostのライフサイクルコールバック内、つまり構造的にUIスレッド保証がある。</summary>
    internal static SceneViewState CreateFromSettings(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return new(){
            ActiveTool=SceneEditorSettings.ReadActiveTool(),
            TransformSpace=SceneEditorSettings.ReadTransformSpace(),
            ProjectionMode=SceneEditorSettings.ReadProjectionMode(),
            NavigationPreset=SceneEditorSettings.ReadNavigationPreset(),
            NavigationSpeedMultiplier=SceneEditorSettings.ReadNavigationSpeedMultiplier(),
            InvertLookX=SceneEditorSettings.ReadInvertLookX(),
            InvertLookY=SceneEditorSettings.ReadInvertLookY(),
        };
    }

    /// <summary>呼び出しはHwndHostのライフサイクルコールバック内、つまり構造的にUIスレッド保証がある。</summary>
    internal void SaveToSettings(){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneEditorSettings.WriteActiveTool(ActiveTool);
        SceneEditorSettings.WriteTransformSpace(TransformSpace);
        SceneEditorSettings.WriteProjectionMode(ProjectionMode);
        SceneEditorSettings.WriteNavigationPreset(NavigationPreset);
        SceneEditorSettings.WriteNavigationSpeedMultiplier(NavigationSpeedMultiplier);
        SceneEditorSettings.WriteInvertLookX(InvertLookX);
        SceneEditorSettings.WriteInvertLookY(InvertLookY);
    }

    internal Vector3 GetEyePosition()=>SceneViewCamera.GetEyePosition(FocusPosition,Yaw,Pitch,FocusDistance);

    internal Matrix4x4 GetView()=>SceneViewCamera.GetView(FocusPosition,Yaw,Pitch,FocusDistance);

    internal Matrix4x4 GetProjection(float AspectRatio){
        float SafeAspect=Math.Max(AspectRatio,0.001f);
        float Far=Math.Max(FocusDistance*FarDistanceFactor,LastKnownBoundsRadius*FarPlaneRadiusFactor);
        return ProjectionMode==SceneProjectionMode.Orthographic
            ?Matrix4x4.CreateOrthographic(OrthographicSize*2f*SafeAspect,OrthographicSize*2f,NearPlane,Far)
            :SceneViewCamera.GetProjection(FovYRadians,SafeAspect,NearPlane,Far);
    }

    /// <summary>
    /// 初回ビルド成功時、およびTargetKeyが変わった再ビルド時に呼ぶ(<c>SceneHwndHost</c>の
    /// <c>PendingCameraReset</c>)。<see cref="SceneCameraFraming"/>と同じ
    /// <c>ComputeDistance</c>/<c>ViewDirection</c>/<c>DefaultFovYRadians</c>を使うため、
    /// 新規シーンを開いた最初のフレームはPhase 0までの固定フレーミングと同じ見た目になる。
    /// </summary>
    internal void ResetToBounds(Vector3 BoundsCenter,float BoundsRadius,Vector3 ViewDirection,float FovYRadians){
        FocusPosition=BoundsCenter;
        FocusDistance=SceneCameraFraming.ComputeDistance(BoundsRadius,FovYRadians);
        this.FovYRadians=FovYRadians;
        OrthographicSize=BoundsRadius;
        LastKnownBoundsRadius=BoundsRadius;

        // Camera.CreateBoundsFitと同じ符号関係(Forward = -ViewDirection)。
        Vector3 Forward=-ViewDirection;
        Yaw=(float)Math.Atan2(Forward.X,Forward.Z);
        Pitch=(float)Math.Asin(Clamp(Forward.Y,-1f,1f));
    }

    /// <summary>
    /// Look(向きのみ)とFly(WASDQE移動)は同一RMBセッション内で同時に起こりうるため1関数に定式化する
    /// (design doc §2の不変条件: Fly中は視点位置を直接動かし、終了時にFocusPositionを再導出する)。
    /// </summary>
    internal void TickFly(
        IReadOnlyCollection<NavigationKey>HeldKeys,float DeltaX,float DeltaY,float DeltaTime,bool Shift,bool Ctrl){
        Vector3 Eye=GetEyePosition(); // 更新前のYaw/Pitch/FocusPosition/FocusDistanceから導出

        // 実機検証(M0020 Phase 1): Pitch(上下)の既定挙動が仕様と逆だったため、Orbit(ApplyOrbit、
        // こちらは実機確認済みで正しい)と共有する ApplyLookDelta 自体は変えず、Fly/Look側の
        // DeltaYだけ符号反転して渡す。InvertLookYはこの訂正後の既定に対する追加反転として機能する。
        (Yaw,Pitch)=SceneViewCamera.ApplyLookDelta(
            Yaw,Pitch,DeltaX,-DeltaY,LookSensitivity,LookSensitivity,InvertLookX,InvertLookY);

        Vector3 MoveDirection=Vector3.Zero;
        Vector3 Forward=SceneViewCamera.GetForward(Yaw,Pitch); // 更新後のYaw/Pitch
        Vector3 FlatRight=SceneViewCamera.GetFlatRight(Yaw);
        if(HeldKeys.Contains(NavigationKey.W)){ MoveDirection+=Forward; }
        if(HeldKeys.Contains(NavigationKey.S)){ MoveDirection-=Forward; }
        if(HeldKeys.Contains(NavigationKey.D)){ MoveDirection+=FlatRight; }
        if(HeldKeys.Contains(NavigationKey.A)){ MoveDirection-=FlatRight; }
        if(HeldKeys.Contains(NavigationKey.E)){ MoveDirection+=Vector3.UnitY; }
        if(HeldKeys.Contains(NavigationKey.Q)){ MoveDirection-=Vector3.UnitY; }

        if(MoveDirection!=Vector3.Zero){
            float Speed=FocusDistance*FlySpeedPerFocusDistance*NavigationSpeedMultiplier
                *(Shift?FastNavigationMultiplier:1f)*(Ctrl?PrecisionNavigationMultiplier:1f);
            Eye+=Vector3.Normalize(MoveDirection)*Speed*DeltaTime;
        }

        FocusPosition=Eye+Forward*FocusDistance; // Eyeから再導出
    }

    /// <summary>FocusPositionは変更しない。EyeはGetEyePosition()が自動的に再導出する。</summary>
    internal void ApplyOrbit(float DeltaX,float DeltaY)=>
        (Yaw,Pitch)=SceneViewCamera.ApplyLookDelta(
            Yaw,Pitch,DeltaX,DeltaY,OrbitSensitivity,OrbitSensitivity,InvertX:false,InvertY:false);

    /// <summary>
    /// FocusPositionだけ更新すればEyeはYaw/Pitch/FocusDistance不変なので自動的に同じ量だけ追従する
    /// (design doc §2「視線方向を変えず画面平面上を移動」)。
    /// </summary>
    internal void ApplyPan(float DeltaX,float DeltaY){
        Vector3 Right=SceneViewCamera.GetFlatRight(Yaw);
        Vector3 Up=SceneViewCamera.GetUp(Yaw,Pitch);
        float Scale=FocusDistance*PanSensitivityPerFocusDistance;
        // 実機検証(M0020 Phase 1): 左右(X)の既定挙動が仕様と逆だったため符号を訂正(上下Yは確認済み)。
        FocusPosition+=((DeltaX*Right)+(DeltaY*Up))*Scale;
    }

    internal void ApplyDolly(int WheelNotches)=>
        FocusDistance=Math.Max(FocusDistance*(float)Math.Pow(DollyNotchFactor,WheelNotches),MinFocusDistance);

    internal void ApplyZoom(int WheelNotches)=>
        OrthographicSize=Math.Max(OrthographicSize*(float)Math.Pow(DollyNotchFactor,WheelNotches),MinFocusDistance);

    internal void ApplyNavigationSpeedAdjust(int WheelNotches)=>
        NavigationSpeedMultiplier=Clamp(
            NavigationSpeedMultiplier*(float)Math.Pow(SpeedAdjustNotchFactor,WheelNotches),
            MinNavigationSpeedMultiplier,MaxNavigationSpeedMultiplier);

    // net472にMath.Clampが無いための自前実装(Camera.cs移植時の定番の落とし穴)。
    private static float Clamp(float Value,float Min,float Max)=>Math.Min(Math.Max(Value,Min),Max);
}
