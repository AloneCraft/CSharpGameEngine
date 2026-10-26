using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// <see cref="GltfImporter.ImportScene"/>で使う、glTFメッシュプリミティブ1つ分のジオメトリ。<see cref="ImportedMesh"/>と異なり
/// マテリアルは含まない。複数ノードが同じベイク済みメッシュ／マテリアルの組を参照できるため、マテリアルは<see cref="ImportedScene"/>内で共有し、別途インデックス化する。
/// </summary>
public sealed class ImportedMeshData {
    public required IReadOnlyList<Vector3>Positions { get; init;}

    public required IReadOnlyList<Vector3>Normals { get; init;}

    /// <summary>glTFのTANGENT規約に従い、xyzは接線方向、wは従接線の符号（+1／-1）を表す。</summary>
    public required IReadOnlyList<Vector4>Tangents { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}

    /// <summary>三角形リスト。要素数は常に3の倍数。</summary>
    public required IReadOnlyList<uint>Indices { get; init;}

    /// <summary>スキニングされたプリミティブのJOINTS_n／WEIGHTS_nによる影響度（ADR-0066）。剛体ジオメトリではnull。</summary>
    public ImportedSkinInfluences?Influences { get; init;}
}
