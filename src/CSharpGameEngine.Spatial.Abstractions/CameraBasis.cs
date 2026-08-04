namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// A camera's semantic basis. <see cref="Forward"/> is the view/look direction - the direction the
/// camera is looking, not a raw "vector" (see Docs/Specifications/Spatial-RUF.md "Public APIの命名"
/// for why this repo avoids unqualified names like <c>Direction</c>/<c>Vector</c>). Deliberately
/// separate from any concrete engine <c>Camera</c> type (e.g.
/// <c>CSharpGameEngine.Graphics.D3D12.Camera</c>) - this is just the RUF-space schema for "what a
/// camera basis means", not a runtime camera controller.
/// </summary>
public readonly struct CameraBasis
{
    /// <summary>The direction the camera is looking - not the view matrix's internal forward axis, which may differ by Transform Convention.</summary>
    public RufVector Forward { get; }

    /// <summary>The camera's reference Up, used to build <see cref="Forward"/>'s orthonormal basis.</summary>
    public RufVector Up { get; }

    public CameraBasis(RufVector forward, RufVector up)
    {
        Forward = forward.Normalize();
        Up = up;
    }
}
