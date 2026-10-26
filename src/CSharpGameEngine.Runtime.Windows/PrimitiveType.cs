namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// <see cref="PrimitiveMesh"/>が生成する、<see cref="CSharpGameEngine.Geometry.ProceduralMeshFactory"/>の形状。
/// ファクトリーの各<c>Create*</c>メソッドに1つずつ対応する。
/// </summary>
public enum PrimitiveType {
    Plane,
    Box,
    Sphere,
    Cylinder,
    Cone,
    Torus,
    Disk,
    Capsule,
}
