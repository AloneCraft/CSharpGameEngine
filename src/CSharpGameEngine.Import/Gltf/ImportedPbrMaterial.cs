using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// <see cref="GltfImporter.ImportScene"/>が使う、glTFのmetallic-roughnessマテリアル。各テクスチャスロットは個別に省略可能
///（PNG／JPEGのエンコード済みバイト列を保持し、デコードしない）。空間的な変化がなく単一の係数にベイクされたマテリアルでは、
/// テクスチャはnull、係数は実値を保持する。<see cref="ImportedMesh.BaseColorTextureBytes"/>／BaseColorFactorの場合と同じ。
/// </summary>
public sealed class ImportedPbrMaterial {
    public required Vector4 BaseColorFactor { get; init;}

    public required float MetallicFactor { get; init;}

    public required float RoughnessFactor { get; init;}

    /// <summary>KHRの強度とニトへの変換を行う前の、glTFの生<c>emissiveFactor</c>。</summary>
    public Vector3 EmissiveFactor { get; init;}

    /// <summary>glTFの<c>KHR_materials_emissive_strength</c>。拡張がない場合は1。</summary>
    public float EmissiveStrength { get; init;}=1f;

    /// <summary>
    /// cd/m²（ニト）で表す、物理的な意味を持つ発光輝度。
    /// <c>EmissiveFactor * EmissiveStrength * GltfImportOptions.EmissiveNitsPerUnit</c>として算出する。
    /// </summary>
    public Vector3 EmissiveNits { get; init;}

    /// <summary>対応する発光テクスチャの線形Rec.709輝度の平均。テクスチャがない場合、または非対応のエンコード形式の場合は1。</summary>
    public float EmissiveTextureLuminanceScale { get; init;}=1f;


    public byte[]?BaseColorTextureBytes { get; init;}

    public byte[]?NormalTextureBytes { get; init;}

    /// <summary>存在する場合に格納する、glTFのエンコード済み<c>emissiveTexture</c>。</summary>
    public byte[]?EmissiveTextureBytes { get; init;}

    /// <summary>glTF標準のパックテクスチャ。G=roughness、B=metallic（Rはocclusionによく使われるが、ここでは未使用）。</summary>
    public byte[]?MetallicRoughnessTextureBytes { get; init;}

    /// <summary>通常のマテリアルでは null、そうでなければ <see cref="BaseColorFactor"/> の上にワールド空間で描画するプロシージャルグリッド（M0098）。</summary>
    public ImportedWorldGrid?WorldGrid { get; init;}
}
