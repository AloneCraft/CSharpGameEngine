using System.Numerics;

namespace CSharpGameEngine.Geometry;

/// <summary>レンダラーへ直接渡せる、位置／法線／インデックスのデータ。これらの基本形状にはインポーターが不要。</summary>
public readonly struct ProceduralMeshData {
    public required Vector3[]Positions { get; init;}

    public required Vector3[]Normals { get; init;}

    public required uint[]Indices { get; init;}
}
