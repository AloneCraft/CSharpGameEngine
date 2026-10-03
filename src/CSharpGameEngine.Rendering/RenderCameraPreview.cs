using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// One camera-relative thumbnail pass embedded into the presentation surface. Rectangles use
/// upper-left surface pixels; the provider clips them to the current surface. The optional
/// excluded object is the authoring camera proxy and is omitted only from this thumbnail.
/// <see cref="DepthMapping"/> selects the pass's depth (M0099): Radial needs <see cref="Radial"/>;
/// RadialObserver also needs <see cref="RadialCenter"/>, the compressed world's centre in render space,
/// and then <see cref="ViewProjection"/> is the observer's rotation-only view times its projection.
/// </summary>
public readonly record struct RenderCameraPreview(
    int X,int Y,int Width,int Height,
    Matrix4x4 ViewProjection,Vector3 CameraPosition,
    int ExcludedObjectIndex=-1,
    RenderDepthMapping DepthMapping=RenderDepthMapping.Standard,
    RadialDepthProjection Radial=default,
    Vector3 RadialCenter=default){
    public const int MaximumCount=3;
}

/// <summary>Observable facts proving that a thumbnail pass was prepared rather than silently lost.</summary>
public readonly record struct RenderCameraPreviewDiagnostics(
    int SceneObjectCount,int FrustumCandidateCount,int DrawCallCount,
    uint ViewProjectionHash);
