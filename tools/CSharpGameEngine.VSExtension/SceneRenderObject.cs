using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Authoring-derived drawable identity and transform. Native render resources belong exclusively
/// to the isolated Render Server and are never retained by the VSIX.
/// </summary>
internal sealed class SceneRenderObject {
    public required SimpleMesh Mesh { get;init;}
    public required Matrix4x4 World { get;init;}
    public required NodePath NodeId { get;init;}
    public bool IncludeInBounds { get;init;}=true;
    public bool IsPickable { get;init;}=true;
}
