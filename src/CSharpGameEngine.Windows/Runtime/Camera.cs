namespace CSharpGameEngine.Runtime.Windows;

/// <summary>Runtime Scene Camera??????Editor Camera??????????????</summary>
public enum CameraProjection {
    Perspective,
    Orthographic,
}

/// <summary>
/// Scene DSL?Runtime ECS?????Camera Component?
/// ???????Node?<see cref="Transform"/>????????????????????????
/// </summary>
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
