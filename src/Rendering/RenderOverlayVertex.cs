using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>カメラ／領域に相対するプレゼンテーション用頂点。オーバーレイの形状は、三角形と線の完全な頂点範囲として別々に渡す。異なる形状が混在する不正な状態が描画境界を越えないようにする。</summary>
public readonly record struct RenderOverlayVertex(Vector3 Position,Vector4 Color);
