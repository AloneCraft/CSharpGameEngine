using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ギズモのハンドル系統を表す(旧仕様§3では排他的な「現在のツール」永続状態だったが、M0033で
/// Move/Rotate/Scaleを常時同時表示にしたためその役目は終わった。値そのものは
/// <c>SceneHwndHost.GetHandleFamily</c>/<c>CanEditTransform</c>/<c>GetTransformMembers</c>/
/// <c>GetToolDisplayName</c>が「系統識別子」として引き続き使う)。<c>Select</c>は現在どのハンドルにも
/// 対応しない既定値としてのみ残っている。
/// </summary>
internal enum SceneActiveTool { Select, Move, Rotate, Scale }

internal enum SceneTransformSpace { World, Local }

internal enum SceneProjectionMode { Perspective, Orthographic }
internal enum SceneViewRenderMode { Lit, BaseColor, Normal, Wireframe }

/// <summary>Unity/Unreal/Blender/Mayaの具体値充填はADR-0041決定事項7によりスコープ外。器のみ用意する。</summary>
internal enum SceneNavigationPreset { Default, Custom }

/// <summary>Fly(RMB+WASDQE)で押下追跡する移動キー。<c>SceneInputStateMachine</c>が保持集合を管理する。</summary>
internal enum NavigationKey { W, A, S, D, Q, E }

/// <summary>
/// Scene View の編集ビュー状態。所有者は<c>SceneHwndHost</c> — Preview Host process/sessionは
/// 再生成され得るため、視点とツール状態はここに置く(ADR-0041 決定事項1)。
///
/// <para>
/// 永続する8項目(<see cref="TransformSpace"/>/<see cref="ProjectionMode"/>/
/// <see cref="NavigationPreset"/>/<see cref="NavigationSpeedMultiplier"/>/<see cref="InvertLookX"/>/
/// <see cref="InvertLookY"/>/<see cref="InvertOrbitX"/>/<see cref="InvertOrbitY"/>)は
/// <see cref="SceneEditorSettings"/>経由でVS再起動を跨いで残る
/// (Docs/Specifications/Scene-Editor-Design.md §10)。それ以外の視点フィールドはセッション内のみ
/// (仕様§20: SceneViewはSceneDeclarationへ保存しない)。M0033で「現在のツール」永続状態
/// (ActiveTool)は廃止した(Move/Rotate/Scaleが常時同時表示になったため)。
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

    // 永続ツール状態(8項目)。
    internal SceneTransformSpace TransformSpace;
    internal SceneProjectionMode ProjectionMode;
    internal SceneNavigationPreset NavigationPreset;
    internal SceneViewRenderMode RenderMode;
    internal bool ShowGlobalSpatialOctree;
    internal float NavigationSpeedMultiplier=1f;
    internal bool InvertLookX;
    internal bool InvertLookY;
    /// <summary>
    /// ピクセル完全一致ドラッグは「右ドラッグでYaw増・下ドラッグでPitch増」の向きでしか成立しない。
    /// 基準(スイッチ無し)は水平・垂直とも逆向きなので、両方を反転しているときに使える。
    /// </summary>
    internal bool LookUsesPixelLock=>this.InvertLookX&&this.InvertLookY;
    internal bool InvertOrbitX;
    internal bool InvertOrbitY;

    // near は SceneCameraFraming と同じ式を使う(M0030 で R1 対策として far 連動へ変更。
    // 定数を二重定義すると片方だけ直る事故になるため、値も式も向こうを唯一の出所にする)。
    private const float FarDistanceFactor=20f;

    // SceneCameraFramingと同値のフロア(Sky dome保護、Phase 0踏襲)。
    private const float FarPlaneRadiusFactor=20f;
    private const float MinFocusDistance=0.01f;
    // RMB+Wheelでの実行時調整(ApplyNavigationSpeedAdjust)と Tools>Options ページの入力検証で
    // 同じ範囲を使う(値の二重定義を作らないためinternal公開している)。
    internal const float MinNavigationSpeedMultiplier=0.05f;
    internal const float MaxNavigationSpeedMultiplier=20f;

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

    // 実機検証(M0020 Phase 2): SceneCameraFraming.ComputeDistanceの1.1倍余白はシーン全体を収める
    // 前提でチューニングされており、シーン全体よりずっと小さい単一オブジェクトへのFocusSelectionに
    // そのまま使うと視野の約84%を占めるほど窮屈に寄ってしまう(実測 Radius=3.5f → 約10.1fユニット、
    // FovY=45°時カバー率cos(FovY/2)/1.1)。ComputeDistance自体は初期シーンフレーミングの既存テストで
    // 固定済みのため変更せず、FocusSelection側だけ追加の余白を掛けて約50%カバー率に緩める。
    private const float FocusSelectionExtraMargin=1.7f;

    /// <summary>呼び出しはHwndHostのライフサイクルコールバック内、つまり構造的にUIスレッド保証がある。</summary>
    internal static SceneViewState CreateFromSettings(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return new(){
            TransformSpace=SceneEditorSettings.ReadTransformSpace(),
            ProjectionMode=SceneEditorSettings.ReadProjectionMode(),
            NavigationPreset=SceneEditorSettings.ReadNavigationPreset(),
            RenderMode=SceneEditorSettings.ReadRenderMode(),
            ShowGlobalSpatialOctree=SceneEditorSettings.ReadShowGlobalSpatialOctree(),
            NavigationSpeedMultiplier=SceneEditorSettings.ReadNavigationSpeedMultiplier(),
            InvertLookX=SceneEditorSettings.ReadInvertLookX(),
            InvertLookY=SceneEditorSettings.ReadInvertLookY(),
            InvertOrbitX=SceneEditorSettings.ReadInvertOrbitX(),
            InvertOrbitY=SceneEditorSettings.ReadInvertOrbitY(),
        };
    }

    /// <summary>呼び出しはHwndHostのライフサイクルコールバック内、つまり構造的にUIスレッド保証がある。</summary>
    internal void SaveToSettings(){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneEditorSettings.WriteTransformSpace(this.TransformSpace);
        SceneEditorSettings.WriteProjectionMode(this.ProjectionMode);
        SceneEditorSettings.WriteNavigationPreset(this.NavigationPreset);
        SceneEditorSettings.WriteRenderMode(this.RenderMode);
        SceneEditorSettings.WriteShowGlobalSpatialOctree(this.ShowGlobalSpatialOctree);
        SceneEditorSettings.WriteNavigationSpeedMultiplier(this.NavigationSpeedMultiplier);
        SceneEditorSettings.WriteInvertLookX(this.InvertLookX);
        SceneEditorSettings.WriteInvertLookY(this.InvertLookY);
        SceneEditorSettings.WriteInvertOrbitX(this.InvertOrbitX);
        SceneEditorSettings.WriteInvertOrbitY(this.InvertOrbitY);
    }

    internal Vector3 GetEyePosition()=>SceneViewCamera.GetEyePosition(this.FocusPosition, this.Yaw, this.Pitch, this.FocusDistance);

    internal Matrix4x4 GetView()=>SceneViewCamera.GetView(this.FocusPosition, this.Yaw, this.Pitch, this.FocusDistance);

    internal Matrix4x4 GetProjection(float AspectRatio){
        var SafeAspect=Math.Max(AspectRatio,0.001f);
        var Far=Math.Max(
                         Math.Max(this.FocusDistance*FarDistanceFactor, this.LastKnownBoundsRadius*FarPlaneRadiusFactor),
                         SceneCameraFraming.BaseNearPlane*2f);
        // 正射投影の深度は線形なので比率調整は要らない。透視だけ far 連動の near にする。
        return this.ProjectionMode==SceneProjectionMode.Orthographic
            ?Matrix4x4.CreateOrthographic(this.OrthographicSize*2f*SafeAspect, this.OrthographicSize*2f,SceneCameraFraming.BaseNearPlane,Far)
            :SceneViewCamera.GetProjection(this.FovYRadians,SafeAspect,SceneCameraFraming.ComputeNearPlane(Far),Far);
    }

    /// <summary>
    /// 初回ビルド成功時、およびTargetKeyが変わった再ビルド時に呼ぶ(<c>SceneHwndHost</c>の
    /// <c>PendingCameraReset</c>)。<see cref="SceneCameraFraming"/>と同じ
    /// <c>ComputeDistance</c>/<c>ViewDirection</c>/<c>DefaultFovYRadians</c>を使うため、
    /// 新規シーンを開いた最初のフレームはPhase 0までの固定フレーミングと同じ見た目になる。
    /// </summary>
    internal void ResetToBounds(
        Vector3 BoundsCenter,float BoundsRadius,Vector3 ViewDirection,float FovYRadians,float AspectRatio){
        this.FocusPosition=BoundsCenter;
        this.FocusDistance=SceneCameraFraming.ComputeDistance(BoundsRadius,FovYRadians,AspectRatio);
        this.FovYRadians=FovYRadians;
        this.OrthographicSize=BoundsRadius;
        this.LastKnownBoundsRadius=BoundsRadius;

        // Camera.CreateBoundsFitと同じ符号関係(Forward = -ViewDirection)。
        var Forward=-ViewDirection;
        this.Yaw=FloatMath.Atan2(Forward.X,Forward.Z);
        this.Pitch=FloatMath.Asin(Clamp(Forward.Y,-1f,1f));
    }

    /// <summary>
    /// M0020 Phase 2 FocusSelection(F)用。<see cref="ResetToBounds"/>と違い視線方向(Yaw/Pitch)と
    /// <see cref="LastKnownBoundsRadius"/>(Far平面のフロア)は変えない — 選択対象がシーン全体より
    /// ずっと小さくても、シーンの他の部分がクリップされないようにする。
    /// <see cref="FocusSelectionExtraMargin"/>で<see cref="SceneCameraFraming.ComputeDistance"/>の
    /// 窮屈な余白を緩める(実機検証で発見 — 詳細は同定数のコメント参照)。
    /// </summary>
    internal void FocusOnBounds(Vector3 BoundsCenter,float BoundsRadius,float AspectRatio){
        this.FocusPosition=BoundsCenter;
        this.FocusDistance=
            SceneCameraFraming.ComputeDistance(BoundsRadius, this.FovYRadians,AspectRatio)*FocusSelectionExtraMargin;
        this.OrthographicSize=BoundsRadius;
    }

    /// <summary>
    /// Look(向きのみ)とFly(WASDQE移動)は同一RMBセッション内で同時に起こりうるため1関数に定式化する
    /// (design doc §2の不変条件: Fly中は視点位置を直接動かし、終了時にFocusPositionを再導出する)。
    /// </summary>
    internal void TickFly(
        IReadOnlyCollection<NavigationKey>HeldKeys,float DeltaX,float DeltaY,float DeltaTime,bool Shift,bool Ctrl){
        var Eye= this.GetEyePosition(); // 更新前のYaw/Pitch/FocusPosition/FocusDistanceから導出

        // 反転スイッチ無しの基準: 右ドラッグでYawが減り、下ドラッグでPitchが下がる(垂直はM0096でユーザーの依頼により、
        // それまでチェックを入れたときの向きを既定にした)。InvertLookX/Yはこの基準に対する反転。
        (this.Yaw, this.Pitch)=SceneViewCamera.ApplyLookDelta(this.Yaw, this.Pitch,-DeltaX,DeltaY,LookSensitivity,LookSensitivity, this.InvertLookX, this.InvertLookY);

        this.ApplyFlyMovement(HeldKeys,Eye,DeltaTime,Shift,Ctrl);
    }

    /// <summary>
    /// ピクセル完全一致ドラッグ(<see cref="ApplyLookPixelLock"/>)使用時のWASDQE専用経路。
    /// 回転は既にムーブイベントごとに<see cref="ApplyLookPixelLock"/>で確定させてあるため、
    /// ここでは<see cref="TickFly"/>から回転更新(<c>ApplyLookDelta</c>呼び出し)だけを除いた
    /// 移動部分のみを毎フレーム適用する。
    /// </summary>
    internal void TickFlyMovementOnly(
        IReadOnlyCollection<NavigationKey>HeldKeys,float DeltaTime,bool Shift,bool Ctrl)=>
        this.ApplyFlyMovement(HeldKeys, this.GetEyePosition(),DeltaTime,Shift,Ctrl);

    /// <summary>
    /// <see cref="TickFly"/>と<see cref="TickFlyMovementOnly"/>で共有するWASDQE移動本体。
    /// ロジックは元の<c>TickFly</c>から1バイトも変えずに抽出しただけ(A/Dの符号反転の経緯を含め
    /// 挙動はすべて据え置き)。
    /// </summary>
    private void ApplyFlyMovement(
        IReadOnlyCollection<NavigationKey>HeldKeys,Vector3 Eye,float DeltaTime,bool Shift,bool Ctrl){
        var MoveDirection=Vector3.Zero;
        var Forward=SceneViewCamera.GetForward(this.Yaw, this.Pitch); // 更新後のYaw/Pitch
        var FlatRight=SceneViewCamera.GetFlatRight(this.Yaw);
        if(HeldKeys.Contains(NavigationKey.W)){ MoveDirection+=Forward; }
        if(HeldKeys.Contains(NavigationKey.S)){ MoveDirection-=Forward; }
        // 実機検証(M0020 Phase 2): A/Dの左右が逆だったため符号を訂正。
        //
        // GetFlatRightは名前に反して画面の「左」へ写るため、画面上で右へ動かすDは-FlatRightを使う
        // (描画が右手系でゲーム本体と一致させているから。SceneViewCamera.GetView参照)。
        // ApplyPanのXが同じ理由で反転しているのと同じ話であり、両方が「符号を反転させている」のは
        // 偶然ではなく同一の規約から来ている。
        if(HeldKeys.Contains(NavigationKey.D)){ MoveDirection-=FlatRight; }
        if(HeldKeys.Contains(NavigationKey.A)){ MoveDirection+=FlatRight; }
        if(HeldKeys.Contains(NavigationKey.E)){ MoveDirection+=Vector3.UnitY; }
        if(HeldKeys.Contains(NavigationKey.Q)){ MoveDirection-=Vector3.UnitY; }

        if(MoveDirection!=Vector3.Zero){
            var Speed= this.FocusDistance*FlySpeedPerFocusDistance* this.NavigationSpeedMultiplier
                     *(Shift?FastNavigationMultiplier:1f)*(Ctrl?PrecisionNavigationMultiplier:1f);
            Eye+=Vector3.Normalize(MoveDirection)*Speed*DeltaTime;
        }

        this.FocusPosition=Eye+Forward* this.FocusDistance; // Eyeから再導出
    }

    /// <summary>
    /// RMBドラッグのピクセル完全一致(M0020追加)。クリック時に捕捉した固定のワールド方向
    /// <paramref name="AnchorWorldDirection"/>が、現在のマウス位置のローカル係数
    /// <paramref name="CurrentLocalAxes"/>(<see cref="SceneViewCamera.DecomposeDirection"/>の
    /// 結果)の位置に来るようYaw/Pitchを直接解いて代入する。<see cref="ApplyLookDelta"/>の
    /// delta*sensitivity方式は経由しない(反転設定がONの間はこちらを使わず、呼び出し元が
    /// 従来の<see cref="TickFly"/>を使い続ける — 反転しながらの完全一致は定義上不可能なため)。
    /// </summary>
    internal void ApplyLookPixelLock(Vector3 AnchorWorldDirection,Vector3 CurrentLocalAxes)=>
        (this.Yaw, this.Pitch)=SceneViewCamera.SolveLookOrientation(CurrentLocalAxes,AnchorWorldDirection, this.Yaw, this.Pitch);

    /// <summary>
    /// FocusPositionは変更しない。EyeはGetEyePosition()が自動的に再導出する。基準はLookと同じ向き
    /// (右ドラッグでYaw減・下ドラッグでPitch減)で、InvertOrbitX/Yはこの基準に対する反転。
    /// </summary>
    internal void ApplyOrbit(float DeltaX,float DeltaY)=>
        (this.Yaw, this.Pitch)=SceneViewCamera.ApplyLookDelta(this.Yaw, this.Pitch,-DeltaX,DeltaY,OrbitSensitivity,OrbitSensitivity, this.InvertOrbitX, this.InvertOrbitY);

    /// <summary>
    /// M0095：<paramref name="Pivot"/>（選択対象の中心）を軸に回転し、画面上の位置を維持する。カメラは <see cref="ApplyOrbit(float,float)"/> と同様に回転し、その後、pivot が以前と同じカメラ空間座標になるよう視点位置を移動する。pivot を中心に剛体回転するため、画面上の位置と距離が変わらず、そこにある球は軌道回転中もまったく同じように描画される。
    /// </summary>
    internal void ApplyOrbit(float DeltaX,float DeltaY,Vector3 Pivot){
        var Offset=Pivot-this.GetEyePosition();
        var Right=SceneViewCamera.GetFlatRight(this.Yaw);
        var Up=SceneViewCamera.GetUp(this.Yaw,this.Pitch);
        var Forward=SceneViewCamera.GetForward(this.Yaw,this.Pitch);
        var Local=new Vector3(Vector3.Dot(Offset,Right),Vector3.Dot(Offset,Up),Vector3.Dot(Offset,Forward));
        this.ApplyOrbit(DeltaX,DeltaY);
        Right=SceneViewCamera.GetFlatRight(this.Yaw);
        Up=SceneViewCamera.GetUp(this.Yaw,this.Pitch);
        Forward=SceneViewCamera.GetForward(this.Yaw,this.Pitch);
        var Eye=Pivot-(Local.X*Right+Local.Y*Up+Local.Z*Forward);
        this.FocusPosition=Eye+Forward*this.FocusDistance;
    }


    /// <summary>
    /// Alt+LMBドラッグのピクセル完全一致(M0020追加、ベストエフォート)。<paramref name="AnchorWorldPoint"/>
    /// (クリック時に捕捉した固定のワールド座標)が、現在のマウス位置
    /// (<paramref name="TargetScreenX"/>,<paramref name="TargetScreenY"/>)へ投影されるYaw/Pitchを
    /// 数値的に解く。OrbitはEye自体が球面上を動くためLook側のような閉形式が存在せず(構造的な性質、
    /// SceneViewCameraTests.TrySolveOrbitYawPitch_NeverIncreasesResidualBeyondTheSeed参照)、
    /// 一時的に完全一致を維持できない区間では「その時点で最も近づける位置」を無条件に採用する
    /// (Levenberg–Marquardt法は残差を絶対に悪化させないことが保証されているため、常に適用してよい)。
    /// FocusPosition/FocusDistanceは毎回ライブに読むため、ドラッグ中にホイールでFocusDistanceが
    /// 変わっても次のムーブイベントで自動的に追従する。
    /// </summary>
    /// <summary>
    /// FocusPositionだけ更新すればEyeはYaw/Pitch/FocusDistance不変なので自動的に同じ量だけ追従する
    /// (design doc §2「視線方向を変えず画面平面上を移動」)。
    /// </summary>
    internal void ApplyPan(float DeltaX,float DeltaY){
        var Right=SceneViewCamera.GetFlatRight(this.Yaw);
        var Up=SceneViewCamera.GetUp(this.Yaw, this.Pitch);
        var Scale= this.FocusDistance*PanSensitivityPerFocusDistance;
        // 実機検証(M0020 Phase 1): 左右(X)の既定挙動が仕様と逆だったため符号を訂正(上下Yは確認済み)。
        //
        // Xに反転が必要な理由(2026-08、Phase 2の実機検証で確定): 描画は右手系の行列を使うため
        // GetFlatRightは名前に反して画面の「左」へ写る(SceneViewCamera.GetView参照)。
        // 「行列を左手系に直せばこの反転は不要になる」と考えて一度実際にやったが、ゲーム本体も
        // 同じ右手系なのでシーンデザイナーだけがゲームと左右反転してしまい差し戻した。
        // つまりこの反転は対症療法ではなく、エンジンの規約に対する正しい変換である。
        this.FocusPosition+=((DeltaX*Right)+(DeltaY*Up))*Scale;
    }

    internal void ApplyDolly(int WheelNotches)=> this.FocusDistance=Math.Max(this.FocusDistance*FloatMath.Pow(DollyNotchFactor,WheelNotches),MinFocusDistance);

    internal void ApplyZoom(int WheelNotches)=> this.OrthographicSize=Math.Max(this.OrthographicSize*FloatMath.Pow(DollyNotchFactor,WheelNotches),MinFocusDistance);

    internal void ApplyNavigationSpeedAdjust(int WheelNotches)=>
        this.NavigationSpeedMultiplier=Clamp(this.NavigationSpeedMultiplier*FloatMath.Pow(SpeedAdjustNotchFactor,WheelNotches),
                                             MinNavigationSpeedMultiplier,MaxNavigationSpeedMultiplier);

    // net472にMath.Clampが無いための自前実装(Camera.cs移植時の定番の落とし穴)。
    private static float Clamp(float Value,float Min,float Max)=>Math.Min(Math.Max(Value,Min),Max);
}
