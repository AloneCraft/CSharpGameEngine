using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// <see cref="ImportedLight"/>のワールド空間での伝播方向を、所有ノードの<see cref="ImportedSceneNode.WorldTransform"/>から求める
///（ADR-0027に従い、既にエンジン空間へ変換済み）。「ローカルForward軸」の知識を各呼び出し側で重複実装せず、ここに集約する。
/// つまりポイントライトのワールド方向は、ノードのローカルRUF Forwardをワールド行列で変換した方向になる。
/// エンジン固定の<see cref="CSharpGameEngine.Spatial.Unity.UnityCoordinateConvention"/>では、ローカルForwardは<c>+Vector3.UnitZ</c>である。
/// glTF固有のローカル<c>-Z</c>ではない。後者が適用されたのは<see cref="GltfAxisConversion.ConvertWorldTransform"/>の実行前だけである。
/// </summary>
public static class ImportedLightDirection {
    public static Vector3 WorldForward(Matrix4x4 EngineSpaceWorldTransform)=>
        Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ,EngineSpaceWorldTransform));
}
