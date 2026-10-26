using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

public sealed class ImportedMesh {
    public required IReadOnlyList<Vector3>Positions { get; init;}

    public required IReadOnlyList<Vector3>Normals { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}

    /// <summary>未デコードの、エンコード済みベースカラー画像データ（PNG／JPEGなど）。マテリアルにテクスチャがない場合はnull。</summary>
    public byte[]?BaseColorTextureBytes { get; init;}

    /// <summary>glTFのbaseColorFactorによる色合い。プリミティブにマテリアルがない場合は(1,1,1,1)。</summary>
    public required Vector4 BaseColorFactor { get; init;}

    /// <summary>三角形リスト。要素数は常に3の倍数。</summary>
    public required IReadOnlyList<uint>Indices { get; init;}

    public required BoundingBox Bounds { get; init;}
}
