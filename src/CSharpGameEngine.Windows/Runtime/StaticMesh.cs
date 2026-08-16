using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// 外部 3D 資産を参照する Scene DSL コンポーネント。
/// 配置は所有 Node の Transform が担い、同じ <see cref="AssetPath"/> を参照する複数 Node は
/// 実行時に同じメッシュ／マテリアル資産を共有する。
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/StaticMesh.svg")]
public struct StaticMesh {
    /// <summary>アプリケーションのベースディレクトリからの相対パス。</summary>
    public string AssetPath;

    public MeshMobility Mobility;
}
