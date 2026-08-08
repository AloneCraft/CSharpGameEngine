using System.Diagnostics.CodeAnalysis;

namespace CSharpGameEngine.Entities;

/// <summary>
/// 実行時に動的生成される1つのコンポーネント型集合分のSoAストレージ。<see cref="World"/>が一度
/// 生成したArchetypeは破棄されない(<see cref="World.ArchetypeCount"/>がappend-onlyであることに
/// <see cref="EntityQuery"/>の差分スキャンが依存しているため)。<see cref="ComponentTypeIds"/>は
/// 昇順ソート済みで、そのまま列の並び順(<see cref="GetColumn"/>のインデックス)にもなる。
/// </summary>
public sealed class Archetype
{
    internal readonly int Index;
    internal readonly int[] ComponentTypeIds;
    internal readonly ComponentMask Mask;

    private readonly IComponentColumn[] columns;
    private readonly Dictionary<int, Archetype> addEdges = [];
    private readonly Dictionary<int, Archetype> removeEdges = [];
    private Entity[] entities = [];

    internal Archetype(int index, int[] componentTypeIds, IComponentColumn[] columns)
    {
        this.Index = index;
        this.ComponentTypeIds = componentTypeIds;
        this.Mask = ComponentMask.FromSortedIds(componentTypeIds);
        this.columns = columns;
    }

    public int Count { get; private set; }

    public ReadOnlySpan<Entity> Entities => this.entities.AsSpan(0, this.Count);

    internal Entity GetEntity(int row)
    {
        return this.entities[row];
    }

    /// <summary>
    /// 2分探索。Queryが新しいArchetypeを発見した瞬間に1回だけ呼ばれる想定であり、
    /// Query一致後の行ループ(ホットパス)からは呼ばない。
    /// </summary>
    internal int IndexOfColumn(int componentTypeId)
    {
        return Array.BinarySearch(this.ComponentTypeIds, componentTypeId);
    }

    internal IComponentColumn GetColumn(int columnIndex)
    {
        return this.columns[columnIndex];
    }

    public Span<T> GetSpan<T>(int columnIndex) where T : struct
    {
        return ((ComponentColumn<T>)this.columns[columnIndex]).Items.AsSpan(0, this.Count);
    }

    public ReadOnlySpan<T> GetReadOnlySpan<T>(int columnIndex) where T : struct
    {
        return ((ComponentColumn<T>)this.columns[columnIndex]).Items.AsSpan(0, this.Count);
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

    internal bool TryGetAddEdge(int componentTypeId, [MaybeNullWhen(false)] out Archetype destination)
    {
        return this.addEdges.TryGetValue(componentTypeId, out destination);
    }

    internal void SetAddEdge(int componentTypeId, Archetype destination)
    {
        this.addEdges[componentTypeId] = destination;
    }

    internal bool TryGetRemoveEdge(int componentTypeId, [MaybeNullWhen(false)] out Archetype destination)
    {
        return this.removeEdges.TryGetValue(componentTypeId, out destination);
    }

    internal void SetRemoveEdge(int componentTypeId, Archetype destination)
    {
        this.removeEdges[componentTypeId] = destination;
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
