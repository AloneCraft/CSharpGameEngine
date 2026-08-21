using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// A unit-sized procedural primitive (Scene DSL component, ADR-0040 Phase 6). Carries no size fields
/// of its own - sizing is entirely the owning Node's Transform.Scale's job, mirroring Unity's
/// GameObject.CreatePrimitive(PrimitiveType) convention rather than a union of per-shape dimension
/// fields (Box's Vector3 size vs. Sphere's single radius vs. Cylinder's radius+height, etc.). See
/// RenderServer.UnitSizeFor for the concrete unit dimensions each PrimitiveType maps to.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,0)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/PrimitiveMesh.svg")]
public struct PrimitiveMesh {
    public PrimitiveType Type;
}
