using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Entities;

/// <summary>
/// フレーム内の正本となる姿勢。シミュレーションはLogical値を保持する。GPU、音声、UIに渡すのは、
/// 完了したティックスナップショットから導出した、ドメイン相対のfloat投影値だけ。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Spatial,0)]
public struct Transform() {
    public ReferenceFrameId Frame=ReferenceFrameId.Root;
    public LogicalVector3 Position=LogicalVector3.Zero;
    public LogicalQuaternion Rotation=LogicalQuaternion.Identity;
    public LogicalVector3 Scale=LogicalVector3.One;
}
