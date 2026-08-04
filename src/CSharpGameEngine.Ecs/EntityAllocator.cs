namespace CSharpGameEngine.Ecs;

/// <summary>
/// Id+Generationの確保・解放だけを行う、アーキタイプに依存しない最小のエンティティアロケータ。
/// <see cref="World"/>が内部に持つ確保ロジックと同じ考え方(Id再利用+Generation増分)を、
/// アーキタイプ非依存のAoSストレージ(<see cref="AosEntityStore{TRow}"/>)から使えるように、
/// あえて共有せず複製する。
/// </summary>
internal sealed class EntityAllocator
{
    private readonly Stack<int> freeIds = [];
    private int[] generations = [];
    private bool[] alive = [];
    private int nextId;

    internal Entity Allocate()
    {
        int id;

        if (this.freeIds.Count > 0)
        {
            id = this.freeIds.Pop();
        }
        else
        {
            id = this.nextId++;
            this.EnsureCapacity(id + 1);
            this.generations[id] = 1;
        }

        this.alive[id] = true;
        return new Entity(id, this.generations[id]);
    }

    internal bool IsAlive(Entity entity)
    {
        return entity.Id >= 0
            && entity.Id < this.nextId
            && this.alive[entity.Id]
            && this.generations[entity.Id] == entity.Generation;
    }

    internal void Validate(Entity entity)
    {
        if (!this.IsAlive(entity))
        {
            throw new InvalidOperationException($"{entity} is not alive.");
        }
    }

    internal void Destroy(Entity entity)
    {
        this.Validate(entity);
        this.alive[entity.Id] = false;
        this.generations[entity.Id]++;
        this.freeIds.Push(entity.Id);
    }

    private void EnsureCapacity(int capacity)
    {
        if (this.generations.Length < capacity)
        {
            int newCapacity = Math.Max(capacity, Math.Max(4, this.generations.Length * 2));
            Array.Resize(ref this.generations, newCapacity);
            Array.Resize(ref this.alive, newCapacity);
        }
    }
}
