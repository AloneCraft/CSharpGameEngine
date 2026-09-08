using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// Camera/domain-relative presentation vertex. Overlay topology is supplied as separate complete
/// triangle and line spans so impossible mixed-topology state cannot cross the render boundary.
/// </summary>
public readonly record struct RenderOverlayVertex(Vector3 Position,Vector4 Color);
