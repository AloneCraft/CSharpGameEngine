namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// glTFシーンのインポート時に行う、物理的な意味を持つ変換を制御する。
/// glTFでは発光単位が表す実世界の輝度を規定していないため、エンジンでは任意のHDR倍率をマテリアルに黙って埋め込まず、
/// このスケールを明示的に指定する。
/// </summary>
public sealed record GltfImportOptions {
    /// <summary>glTFの発光単位1つをcd/m²（ニト）へ換算する既定値。</summary>
    public const float DefaultEmissiveNitsPerUnit=100f;

    /// <summary>
    /// glTFの<c>emissiveFactor == (1,1,1)</c>と発光強度1が表す輝度。
    /// 有限の正の値でなければならない。
    /// </summary>
    public float EmissiveNitsPerUnit { get;init;}=DefaultEmissiveNitsPerUnit;

    internal void Validate(){
        if(!float.IsFinite(this.EmissiveNitsPerUnit)||this.EmissiveNitsPerUnit<=0f){
            throw new ArgumentOutOfRangeException(nameof(this.EmissiveNitsPerUnit),"EmissiveNitsPerUnit must be finite and greater than zero.");
        }
    }
}
