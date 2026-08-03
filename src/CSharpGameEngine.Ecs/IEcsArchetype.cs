namespace CSharpGameEngine.Ecs;

/// <summary>
/// EcsArchetypeGeneratorが生成する各アーキタイプ構造体が実装するCRTP契約。
/// <see cref="World"/>はこの契約だけを通じてアーキタイプの正規化済みコンポーネント集合・
/// ストレージ列・ビュー構築方法を知り、個々のアーキタイプの形状を一切ハードコードしない。
/// </summary>
public interface IEcsArchetype<TSelf> where TSelf : struct, IEcsArchetype<TSelf>
{
    /// <summary>
    /// 正規化(コンポーネント型の完全修飾名によるOrdinal順)済みのコンポーネント型配列。
    /// このシーケンス自体が<see cref="World"/>にとってのアーキタイプキーになる。
    /// </summary>
    static abstract Type[] ComponentTypes { get; }

    /// <summary>
    /// <see cref="ComponentTypes"/>と同じ順序で、コンポーネント型ごとの<see cref="ComponentColumn{T}"/>を生成する。
    /// </summary>
    static abstract IComponentColumn[] CreateColumns();

    /// <summary>
    /// 指定した<see cref="World"/>・<see cref="Entity"/>を参照するビュー構造体を構築する。
    /// </summary>
    static abstract TSelf CreateView(World world, Entity entity);
}
