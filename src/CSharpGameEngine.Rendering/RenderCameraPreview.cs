using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// One camera-relative thumbnail pass embedded into the presentation surface. Rectangles use
/// upper-left surface pixels; the provider clips them to the current surface. The optional
/// excluded object is the authoring camera proxy and is omitted only from this thumbnail.
/// <see cref="Depth"/> carries the validated encoding for this projection. An observer also carries
/// its compressed world's centre in the depth packet, and <see cref="ViewProjection"/> is its
/// rotation-only view times its projection.
/// <see cref="MarkRadialCenter"/> draws the main camera's frustum at the observer's centre.
/// </summary>
public readonly record struct RenderCameraPreview(
    int X,int Y,int Width,int Height,
    Matrix4x4 ViewProjection,Vector3 CameraPosition,RenderViewDepth Depth,
    int ExcludedObjectIndex=-1,
    bool MarkRadialCenter=false){
    public const int MaximumCount=3;
}

/// <summary>Observable facts proving that a thumbnail pass was prepared rather than silently lost.</summary>
public readonly record struct RenderCameraPreviewDiagnostics(
    int SceneObjectCount,int FrustumCandidateCount,int DrawCallCount,
    uint ViewProjectionHash);
