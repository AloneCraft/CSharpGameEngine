using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows;

/// <summary>Runtime Scene Cameraの投影方式。Editor Camera(編集ビュー)とは独立した概念。</summary>
public enum CameraProjection {
    Perspective,
    Orthographic,
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
}
