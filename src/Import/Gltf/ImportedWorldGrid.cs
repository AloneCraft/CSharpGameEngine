using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// テクスチャに焼き付ける代わりにワールド空間で描画するプロシージャルグリッド（M0098）。Unreal の試作グリッドマテリアルは、タイル状のワイヤーテクスチャをワールドに整列させて投影する。床全体を
/// 1024 pxで焼き付けても同じ結果にならないため、エンジンはピクセルごとに線を再計算する。マテリアルのベースカラー係数は線の間の表面色を表す。glTF では
/// <c>materials[].extras.CGE_world_grid</c> に格納する。
/// </summary>
/// <param name="GridSizeMeters">主グリッド線の間隔（メートル）。</param>
/// <param name="SubGridDivisions">主グリッドの1区画に含める補助グリッド区画の数。</param>
/// <param name="WireHalfWidth">各区画の幅に対する線幅の半分の割合。主グリッドと補助グリッドで共通。</param>
/// <param name="GridColor">主グリッド線のリニア RGB 色。</param>
/// <param name="SubGridColor">補助グリッド線のリニア RGB 色。</param>
public readonly record struct ImportedWorldGrid(float GridSizeMeters,int SubGridDivisions,float WireHalfWidth,Vector3 GridColor,Vector3 SubGridColor){
    /// <summary>グリッドを格納するマテリアル extras のキー。</summary>
    public const string ExtrasKey="CGE_world_grid";
}
