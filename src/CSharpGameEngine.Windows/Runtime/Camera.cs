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
[SceneComponentIcon("Assets/Editor/ComponentIcons/Camera.svg")]
public struct Camera {
    public bool Active;
    public CameraProjection Projection;
    public float FovYRadians;
    public float NearZ;
    public float FarZ;
    public float OrthographicSize;

    public Camera(){
        this.Active=true;
        this.Projection=CameraProjection.Perspective;
        this.FovYRadians=System.MathF.PI/4f;
        this.NearZ=0.1f;
        this.FarZ=300f;
        this.OrthographicSize=10f;
    }
}
