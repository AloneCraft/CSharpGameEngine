namespace CSharpGameEngine.Ecs;

/// <summary>
/// 正規化済みコンポーネント型集合1つ分のSoAストレージ。同じ構成を持つ複数の名前付きアーキタイプ
/// (例: プロパティ宣言順だけが異なる2つのアーキタイプ)から、実行時に1つだけ生成・共有される。
/// </summary>
internal sealed class ArchetypeStorage
{
    internal readonly Type[] ComponentTypes;
    private readonly IComponentColumn[] columns;
    private Entity[] entities = [];

    internal int Count { get; private set; }

    internal ArchetypeStorage(Type[] componentTypes, IComponentColumn[] columns)
    {
        this.ComponentTypes = componentTypes;
        this.columns = columns;
    }

    internal IComponentColumn GetColumn(int columnIndex)
    {
        return this.columns[columnIndex];
    }

    internal int AddRow(Entity entity)
    {
        int row = this.Count;
        this.EnsureCapacity(row + 1);
        this.entities[row] = entity;
        this.Count = row + 1;
        return row;
    }

    /// <summary>
    /// 指定行を、末尾行を詰めることで削除する(swap-back)。末尾行が移動した場合、
    /// 移動先に来たエンティティを呼び出し元に返すので、Worldの位置テーブルを更新できる。
    /// </summary>
    internal bool RemoveRowSwapBack(int row, out Entity movedEntity)
    {
        int lastRow = this.Count - 1;
        bool didMove = row != lastRow;

        if (didMove)
        {
            foreach (IComponentColumn column in this.columns)
            {
                column.MoveRow(lastRow, row);
            }

            this.entities[row] = this.entities[lastRow];
        }

        movedEntity = didMove ? this.entities[row] : default;
        this.Count = lastRow;
        return didMove;
    }

    private void EnsureCapacity(int capacity)
    {
        if (this.entities.Length < capacity)
        {
            int newCapacity = Math.Max(capacity, Math.Max(4, this.entities.Length * 2));
            Array.Resize(ref this.entities, newCapacity);

            foreach (IComponentColumn column in this.columns)
            {
                column.EnsureCapacity(newCapacity);
            }
        }
    }
}
