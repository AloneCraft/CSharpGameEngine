namespace CSharpGameEngine.Entities;

/// <summary>
/// コンポーネント型ごとにただ1種類だけ存在する汎用SoA列実装。実行時に動的生成されるどの
/// <see cref="Archetype"/>も、同じコンポーネント型集合であれば同じ列レイアウトを使う。
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

    public void CopyRowFrom(IComponentColumn source, int sourceRow, int destinationRow)
    {
        this.Items[destinationRow] = ((ComponentColumn<T>)source).Items[sourceRow];
    }

    public void SetDefault(int row)
    {
        this.Items[row] = default;
    }
}
