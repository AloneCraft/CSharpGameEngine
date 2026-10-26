using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 単位サイズのプロシージャルプリミティブ（Scene DSLコンポーネント、ADR-0040 Phase 6）。サイズ用のフィールドは持たず、
/// サイズは所有NodeのTransform.Scaleだけで決める。形状ごとの寸法フィールド（BoxのVector3サイズ、Sphereの単一半径、Cylinderの半径と高さなど）を
/// まとめた共用体ではなく、UnityのGameObject.CreatePrimitive(PrimitiveType)規約に合わせる。
/// 各PrimitiveTypeに対応する具体的な単位寸法はPrimitiveRosterを参照。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,0)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/PrimitiveMesh.svg")]
public struct PrimitiveMesh {
    public PrimitiveType Type;
}
