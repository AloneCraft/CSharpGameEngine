namespace CSharpGameEngine.Runtime.Windows;
/// <summary>Which <see cref="CSharpGameEngine.Geometry.ProceduralMeshFactory"/> shape a <see cref="PrimitiveMesh"/> generates. One entry per <c>Create*</c> method on that factory.</summary>
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
