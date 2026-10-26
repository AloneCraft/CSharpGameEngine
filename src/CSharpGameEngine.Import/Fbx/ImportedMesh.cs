using System.Numerics;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// <c>CSharpGameEngine.Import.Gltf.ImportedMesh</c>と同じ形にし、共有／相互参照依存を設けずに、呼び出し側がglTFとFBXのインポート結果を統一的に扱えるようにする。
/// 意図的に定義を重複させている（ADR-0004 §3の前例とADR-0009を参照）。
/// </summary>
public sealed class ImportedMesh {
    public required IReadOnlyList<Vector3>Positions { get; init;}

    public required IReadOnlyList<Vector3>Normals { get; init;}

    public required IReadOnlyList<Vector2>Uvs { get; init;}

    /// <summary>未デコードの、エンコード済みベースカラー画像データ（PNG／JPEGなど）。マテリアルに埋め込みテクスチャがない場合はnull。</summary>
    public byte[]?BaseColorTextureBytes { get; init;}

    /// <summary>ベースカラーの色合い。メッシュにマテリアルがない場合は(1,1,1,1)。</summary>
    public required Vector4 BaseColorFactor { get; init;}

    /// <summary>三角形リスト。要素数は常に3の倍数。</summary>
    public required IReadOnlyList<uint>Indices { get; init;}

    public required BoundingBox Bounds { get; init;}
}
