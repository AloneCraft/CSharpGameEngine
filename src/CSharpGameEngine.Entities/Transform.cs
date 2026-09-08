using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Entities;

/// <summary>
/// Authoritative frame-local pose. Simulation retains Logical values; GPU, audio, and UI receive
/// only domain-relative float projections derived from a completed tick snapshot.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Spatial,0)]
public struct Transform() {
    public ReferenceFrameId Frame=ReferenceFrameId.Root;
    public LogicalVector3 Position=LogicalVector3.Zero;
    public LogicalQuaternion Rotation=LogicalQuaternion.Identity;
    public LogicalVector3 Scale=LogicalVector3.One;
}
