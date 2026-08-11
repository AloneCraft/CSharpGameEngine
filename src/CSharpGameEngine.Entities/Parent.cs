namespace CSharpGameEngine.Entities;

/// <summary>
/// 親子関係を表す汎用Component(Unity DOTSの<c>Unity.Transforms.Parent</c>と同じ位置づけ)。
/// このECS自体はHierarchy/親子の概念を持たないため、<c>SceneDefinitionGenerator</c>が
/// Scene定義の入れ子構造から自動的にこのComponentを子Entityへ付与する。ユーザーが
/// 直接<c>Set</c>する必要はない。
/// </summary>
public struct Parent {
    public Entity Value;
}
