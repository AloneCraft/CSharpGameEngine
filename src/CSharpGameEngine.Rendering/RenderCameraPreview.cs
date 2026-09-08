using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// One camera-relative thumbnail pass embedded into the presentation surface. Rectangles use
/// upper-left surface pixels; the provider clips them to the current surface. The optional
/// excluded object is the authoring camera proxy and is omitted only from this thumbnail.
/// </summary>
public readonly record struct RenderCameraPreview(
    int X,int Y,int Width,int Height,
    Matrix4x4 ViewProjection,Vector3 CameraPosition,
    int ExcludedObjectIndex=-1){
    public const int MaximumCount=3;
}

/// <summary>Observable facts proving that a thumbnail pass was prepared rather than silently lost.</summary>
public readonly record struct RenderCameraPreviewDiagnostics(
    int SceneObjectCount,int FrustumCandidateCount,int DrawCallCount,
    uint ViewProjectionHash);
