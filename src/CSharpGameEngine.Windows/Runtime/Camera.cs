using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows;

/// <summary>Runtime Scene Cameraの投影方式。Editor Camera(編集ビュー)とは独立した概念。</summary>
public enum CameraProjection {
    Perspective,
    Orthographic,
}

/// <summary>
/// 距離を深度へ変える方式(M0099, ADR-0076)。Radialは画像を変えずに、ミリメートルからQ64.64の範囲までを
/// 1枚の深度で正しく前後させる。
/// </summary>
public enum CameraDepthMapping {
    Standard,
    Radial,
}

/// <summary>
/// Scene DSLとRuntime ECSで使うCamera Component。
/// 姿勢は自身では持たず、同じNodeの<see cref="Transform"/>から位置・向きを取る(投影パラメータ専用)。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Camera,0)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/Camera.svg")]
public struct Camera() {
    public bool Active=true;
    public CameraProjection Projection=CameraProjection.Perspective;
    [Angle(AngleUnit.Radians)]
    public float FovYRadians=System.MathF.PI/4f;
    public float NearZ=0.1f;
    public float FarZ=300f;
    public float OrthographicSize=10f;
    /// <summary>物理照明をHDRへ写像する露出補正[EV100]。</summary>
    public float ExposureEv100=0f;
    /// <summary>
    /// 深度方式。既定はRadialで、<see cref="NearZ"/>と<see cref="FarZ"/>をどれだけ離してもよい(エディタのビューもこの範囲を含む)。
    /// Standardは明示的な選択(ADR-0076 追記、M0100)。
    /// </summary>
    public CameraDepthMapping DepthMapping=CameraDepthMapping.Radial;
    /// <summary>Radialの曲線 log2(1 + r/T) が線形から対数へ移る距離T[m]。</summary>
    public float RadialTransitionMeters=1f;
}
