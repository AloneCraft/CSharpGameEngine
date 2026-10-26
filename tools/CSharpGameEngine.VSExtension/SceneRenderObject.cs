using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 作成内容から得られる描画可能な識別情報と Transform。ネイティブなレンダーリソースは分離された Render Server だけが所有し、VSIX が保持することはない。
/// </summary>
internal sealed class SceneRenderObject {
    public required SimpleMesh Mesh { get;init;}
    public required Matrix4x4 World { get;init;}
    public required NodePath NodeId { get;init;}
    public bool IncludeInBounds { get;init;}=true;
    public bool IsPickable { get;init;}=true;
}
