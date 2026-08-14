using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene View の編集カメラ数学。<c>src/CSharpGameEngine.Windows/Graphics/Camera.cs</c>の
/// <c>CSharpGameEngine.Graphics.D3D12.Camera</c>(net10.0、`[SceneFactory]`属性と
/// `CSharpGameEngine.Spatial.Abstractions`/`.Unity`のRUF経路に依存。Scene DSLのComponent
/// `CSharpGameEngine.Runtime.Windows.Camera`とは別物)の閉形式を net472 側へ書き写したもの
/// (ADR-0041 決定事項1、GizmoMeshFactory と同じ複製方針)。RUF型・`[SceneFactory]`属性は
/// 一切持ち込まない。
///
/// <para>
/// 状態は持たない静的クラスにしてある(<see cref="SceneCameraFraming"/>と同型)。状態の唯一の
/// 所有者は <c>SceneViewState</c> — Yaw/Pitch/FocusPosition/FocusDistance をこちらにも複製すると
/// 「どちらが正か」という二重の真実源が生まれるため。
/// </para>
///
/// <para>
/// <c>MathF</c>・<c>Math.Clamp</c>はいずれも net472 に存在しないため <c>(float)Math.*</c> と
/// 手書きクランプを使う(<see cref="SceneCameraFraming"/>が既に実践している回避)。
/// </para>
/// </summary>
internal static class SceneViewCamera {
    // Orbit(±80°/40°)を採用する。このカメラはOrbit/Focus中心の編集カメラであり、Fly(WASDQE)中も
    // Pitchクランプ範囲は変えない — 仕様にFly専用のPitch範囲の言及は無く、Look→Fly→Lookの間で
    // Pitchの意味が不連続になるのを避けるため。CreateFirstPerson用の±85°
    // (legacy Camera.MinPitchRadiansFirstPerson)は無関係なプレイヤー一人称カメラのものであり、
    // このエディタカメラには適用しない。
    internal const float MinPitchRadians=-80f*(float)(Math.PI/180.0);
    internal const float MaxPitchRadians=40f*(float)(Math.PI/180.0);

    /// <summary>
    /// ピッチを含む完全な前方向。CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep
    /// が固定する閉形式 Forward=normalize(cosPitch*sinYaw, sinPitch, cosPitch*cosYaw) と同一。
    /// </summary>
    internal static Vector3 GetForward(float Yaw,float Pitch){
        var CosPitch=(float)Math.Cos(Pitch);
        return Vector3.Normalize(new Vector3(
            CosPitch*(float)Math.Sin(Yaw),
            (float)Math.Sin(Pitch),
            CosPitch*(float)Math.Cos(Yaw)));
    }

    /// <summary>ピッチを除いたXZ平面上の前方向(Fly移動の水平基準)。</summary>
    internal static Vector3 GetFlatForward(float Yaw)=>
        Vector3.Normalize(new Vector3((float)Math.Sin(Yaw),0f,(float)Math.Cos(Yaw)));

    /// <summary>
    /// 水平右方向。CameraTests.GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep が
    /// 固定する閉形式 FlatRight=normalize(Cross(UnitY,FlatForward)) と同一。
    /// </summary>
    internal static Vector3 GetFlatRight(float Yaw)=>
        Vector3.Normalize(Vector3.Cross(Vector3.UnitY,GetFlatForward(Yaw)));

    /// <summary>
    /// カメラ相対のUpベクトル(Pan専用、新規導出 — legacy Camera.cs に対応物なし)。
    /// Cross(UnitY,Forward)=(Fz,0,-Fx) はForwardのY成分(Pitch由来)に依存しないため、
    /// GetFlatRight はPitchに関わらず常に正しいカメラ右ベクトルになる。UpはこのRightと
    /// Forwardの外積で求める(Matrix4x4.CreateLookAt相当のLH基底構成と同型)。
    /// </summary>
    internal static Vector3 GetUp(float Yaw,float Pitch)=>
        Vector3.Normalize(Vector3.Cross(GetForward(Yaw,Pitch),GetFlatRight(Yaw)));

    internal static Vector3 GetEyePosition(Vector3 FocusPosition,float Yaw,float Pitch,float FocusDistance)=>
        FocusPosition-GetForward(Yaw,Pitch)*FocusDistance;

    internal static Matrix4x4 GetView(Vector3 FocusPosition,float Yaw,float Pitch,float FocusDistance){
        Vector3 Eye=GetEyePosition(FocusPosition,Yaw,Pitch,FocusDistance);
        Vector3 LookAtPoint=FocusDistance>1e-4f?FocusPosition:Eye+GetForward(Yaw,Pitch);
        return Matrix4x4.CreateLookAt(Eye,LookAtPoint,Vector3.UnitY);
    }

    internal static Matrix4x4 GetProjection(float FovYRadians,float AspectRatio,float NearZ,float FarZ)=>
        Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians,AspectRatio,NearZ,FarZ);

    /// <summary>
    /// 生のマウス移動量をYaw/Pitchへ適用する。DeltaYを減算するのはlegacy Camera.ApplyMouseDeltaと
    /// 同じ規約(画面下方向のドラッグ=ピッチダウン)。Pitchのみクランプする。
    /// </summary>
    internal static(float Yaw,float Pitch)ApplyLookDelta(
        float Yaw,float Pitch,float DeltaX,float DeltaY,
        float YawSensitivity,float PitchSensitivity,bool InvertX,bool InvertY){
        float NewYaw=Yaw+DeltaX*YawSensitivity*(InvertX?-1f:1f);
        float NewPitch=Math.Min(Math.Max(
            Pitch-DeltaY*PitchSensitivity*(InvertY?-1f:1f),
            MinPitchRadians),MaxPitchRadians);
        return(NewYaw,NewPitch);
    }
}
