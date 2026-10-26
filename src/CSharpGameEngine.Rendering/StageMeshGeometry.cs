using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>Portable geometry preparation shared by mesh submission paths.</summary>
public static class StageMeshGeometry {
    /// <summary>An arbitrary unit tangent perpendicular to a normal for meshes without a UV-aligned normal map.</summary>
    public static Vector4 ArbitraryTangentFor(Vector3 Normal){
        var Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
        var Tangent=Vector3.Normalize(Vector3.Cross(Reference,Normal));
        return new Vector4(Tangent,1f);
    }
}
