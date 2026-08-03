namespace CSharpGameEngine.Ecs;

/// <summary>
/// 引数なし版<c>Create&lt;T&gt;()</c>を全アーキタイプ共通の唯一の実装として提供する。
/// <see cref="World"/>自身は<c>Create</c>という名前のインスタンスメンバーを一切持たない
/// (1つでも持つと、この名前に対する拡張メソッド解決がすべて隠蔽されてしまうため)。
/// 名前付き引数版のコンストラクタ形式はEcsArchetypeGeneratorがアーキタイプごとに
/// 個別の拡張メソッドとして生成する(こちらも同じ理由でWorldのインスタンスメンバーにはしない)。
/// </summary>
public static class WorldEcsExtensions
{
    public static T Create<T>(this World world) where T : struct, IEcsArchetype<T>
    {
        Entity entity = world.CreateEntityCore<T>();
        return T.CreateView(world, entity);
    }
}
