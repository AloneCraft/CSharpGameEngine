namespace CSharpGameEngine.Entities;

/// <summary>
/// Set呼び出しを1つも持たないScene Nodeに、SceneDefinitionGeneratorが自動的に付与する
/// 0フィールドタグComponent。World.CreateArchetypeは型引数0個のオーバーロードを持たないため、
/// 空ノードを成立させるために必要(ユーザーが直接Setする必要はない)。
/// </summary>
public readonly struct SceneNodeTag
{
}
