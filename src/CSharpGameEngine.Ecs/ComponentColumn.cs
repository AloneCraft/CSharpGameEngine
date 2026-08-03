namespace CSharpGameEngine.Ecs;

/// <summary>
/// コンポーネント型ごとにただ1種類だけ存在する汎用SoA列実装。
/// アーキタイプ専用のストレージクラスはEcsArchetypeGeneratorが生成することはなく、
/// どのアーキタイプも同じ<see cref="ComponentColumn{T}"/>をコンポーネント型ごとに1つずつ束ねて使う。
/// これにより、正規化後のコンポーネント型集合が一致する複数のアーキタイプは、
/// 実行時に自然に同一の<see cref="ArchetypeStorage"/>を共有できる。
/// </summary>
public sealed class ComponentColumn<T> : IComponentColumn where T : struct
{
    public T[] Items = [];

    public void EnsureCapacity(int capacity)
    {
        if (this.Items.Length < capacity)
        {
            int newCapacity = Math.Max(capacity, Math.Max(4, this.Items.Length * 2));
            Array.Resize(ref this.Items, newCapacity);
        }
    }

    public void MoveRow(int sourceRow, int destinationRow)
    {
        this.Items[destinationRow] = this.Items[sourceRow];
    }
}
