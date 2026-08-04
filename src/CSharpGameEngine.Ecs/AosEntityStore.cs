namespace CSharpGameEngine.Ecs;

/// <summary>
/// AoS(Array of Structs)物理ストレージの汎用実装。GenerateEntityGeneratorがAoSバックエンドを
/// 選択したEntity型ごとに、行構造体<typeparamref name="TRow"/>を指定してこの型を1つ内包する。
/// <see cref="Entity"/>は行配列の直接Indexではなく、常にLocation Table経由で行番号へ解決する。
/// 削除はswap-backで行い、移動したEntityのLocationだけを更新する。
/// </summary>
public sealed class AosEntityStore<TRow> where TRow : struct
{
    private readonly EntityAllocator entityAllocator = new();
    private TRow[] rows = [];
    private Entity[] rowEntities = [];
    private int[] locations = [];
    private int count;

    public int Count => this.count;

    public Entity Create(in TRow initialValue)
    {
        Entity entity = this.entityAllocator.Allocate();
        this.EnsureEntityCapacity(entity.Id + 1);

        int row = this.count;
        this.EnsureRowCapacity(row + 1);
        this.count = row + 1;

        this.rows[row] = initialValue;
        this.rowEntities[row] = entity;
        this.locations[entity.Id] = row;

        return entity;
    }

    public bool IsAlive(Entity entity)
    {
        return this.entityAllocator.IsAlive(entity);
    }

    public void Destroy(Entity entity)
    {
        this.entityAllocator.Validate(entity);

        int removedRow = this.locations[entity.Id];
        int lastRow = this.count - 1;

        if (removedRow != lastRow)
        {
            this.rows[removedRow] = this.rows[lastRow];

            Entity movedEntity = this.rowEntities[lastRow];
            this.rowEntities[removedRow] = movedEntity;
            this.locations[movedEntity.Id] = removedRow;
        }

        this.rows[lastRow] = default;
        this.rowEntities[lastRow] = default;
        this.count--;

        this.entityAllocator.Destroy(entity);
    }

    public ref TRow GetRowRef(Entity entity)
    {
        this.entityAllocator.Validate(entity);
        return ref this.rows[this.locations[entity.Id]];
    }

    public Span<TRow> AsRowsSpan()
    {
        return this.rows.AsSpan(0, this.count);
    }

    public ReadOnlySpan<Entity> AsEntitiesSpan()
    {
        return this.rowEntities.AsSpan(0, this.count);
    }

    private void EnsureRowCapacity(int required)
    {
        if (this.rows.Length < required)
        {
            int newCapacity = Math.Max(required, Math.Max(4, this.rows.Length * 2));
            Array.Resize(ref this.rows, newCapacity);
            Array.Resize(ref this.rowEntities, newCapacity);
        }
    }

    private void EnsureEntityCapacity(int required)
    {
        if (this.locations.Length < required)
        {
            int newCapacity = Math.Max(required, Math.Max(4, this.locations.Length * 2));
            Array.Resize(ref this.locations, newCapacity);
        }
    }
}
