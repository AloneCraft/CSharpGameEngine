using System.Numerics;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// glTF シーングラフから展開した、描画可能要素またはライトのインスタンス。<see cref="WorldTransform"/> は SharpGLTF の Node.WorldMatrix と
/// 同様、元ノードの親階層をたどって解決済みである。そのため利用側が階層をたどったり、変換を合成したりする必要はない。元の glTF ノードに複数のメッシュプリミティブがある場合、同じ
/// <see cref="Name"/> と <see cref="WorldTransform"/> を共有し、<see cref="MeshIndex"/> と
/// <see cref="MaterialIndex"/> が異なる <see cref="ImportedSceneNode"/> を複数作る。各プリミティブは個別に描画するためである。
/// </summary>
public sealed class ImportedSceneNode {
    public required string Name { get; init;}

    public required Matrix4x4 WorldTransform { get; init;}

    /// <summary><see cref="ImportedScene.Meshes"/> 内のインデックス。ライトのみのノードでは null。</summary>
    public int?MeshIndex { get; init;}

    /// <summary><see cref="ImportedScene.Materials"/> 内のインデックス。<see cref="MeshIndex"/> が null の場合に限り null。</summary>
    public int?MaterialIndex { get; init;}

    /// <summary><see cref="ImportedScene.Lights"/> 内のインデックス。メッシュノードでは null。</summary>
    public int?LightIndex { get; init;}

    /// <summary>
    /// スキニングメッシュノードの <see cref="ImportedScene.Skins"/> 内のインデックス。形状はスキンによって配置されるため、
    /// <see cref="WorldTransform"/> は適用しない（glTF 2.0 skinning）。
    /// </summary>
    public int?SkinIndex { get; init;}
}
