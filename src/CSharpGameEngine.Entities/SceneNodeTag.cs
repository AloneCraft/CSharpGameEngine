namespace CSharpGameEngine.Entities;

/// <summary>
/// Set呼び出しを1つも持たないScene Nodeに、GameSystemGeneratorが自動的に付与する
/// 0フィールドタグComponent。Archetypeは1個以上のComponentを持つ必要があるため、空ノードを
/// 成立させるために必要(ユーザーが直接Setする必要はない)。
/// </summary>
public readonly struct SceneNodeTag {
}
