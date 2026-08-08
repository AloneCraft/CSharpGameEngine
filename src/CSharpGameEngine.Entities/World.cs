namespace CSharpGameEngine.Entities;

/// <summary>
/// エンティティアロケータ(Id再利用+Generation)と、実行時に動的生成されるArchetypeの集合を保持する。
/// Archetypeは一度生成されると破棄されない(<see cref="EntityQuery"/>の差分スキャンがこの
/// append-only性に依存する)。構造変更(Component追加・削除)の即時APIはinternalであり、
/// システムコードからは<see cref="Commands"/>(<see cref="EntityCommandBuffer"/>)経由でのみ触れる
/// (Query一致後の行ループ中に列配列がArray.Resizeされて無効化される事故を設計で防ぐため)。
/// </summary>
public sealed class World
{
    private struct EntityLocation
    {
        internal Archetype? Archetype;
        internal int Row;
    }

    private readonly Dictionary<ComponentTypeSet, Archetype> archetypesBySignature = [];
    private readonly List<Archetype> archetypes = [];
    private readonly Stack<int> freeIds = [];
    private int[] generations = [];
    private EntityLocation[] locations = [];
    private int nextId;

    public World()
    {
        this.Commands = new EntityCommandBuffer();
    }

    public EntityCommandBuffer Commands { get; }

    public int ArchetypeCount => this.archetypes.Count;

    internal Archetype GetArchetype(int index) => this.archetypes[index];

    public EntityArchetype CreateArchetype<T1>()
        where T1 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2>()
        where T1 : struct
        where T2 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id, ComponentType<T2>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2, T3>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2, T3, T4>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2, T3, T4, T5>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2, T3, T4, T5, T6>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2, T3, T4, T5, T6, T7>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
        where T7 : struct
    {
        Span<int> ids = [ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id, ComponentType<T7>.Id];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public EntityArchetype CreateArchetype<T1, T2, T3, T4, T5, T6, T7, T8>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
        where T7 : struct
        where T8 : struct
    {
        Span<int> ids =
        [
            ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id,
            ComponentType<T5>.Id, ComponentType<T6>.Id, ComponentType<T7>.Id, ComponentType<T8>.Id,
        ];
        return new EntityArchetype(this.GetOrCreateArchetype(SortedDistinct(ids)));
    }

    public Entity CreateEntity(EntityArchetype archetype)
    {
        Archetype target = archetype.Archetype;
        int id = this.AllocateId();
        Entity entity = new(id, this.generations[id]);
        int row = target.AddRow(entity);
        this.locations[id] = new EntityLocation { Archetype = target, Row = row };
        return entity;
    }

    public bool IsAlive(Entity entity)
    {
        return entity.Id >= 0
            && entity.Id < this.nextId
            && this.generations[entity.Id] == entity.Generation
            && this.locations[entity.Id].Archetype is not null;
    }

    public bool Has<T>(Entity entity) where T : struct
    {
        this.ValidateAlive(entity);
        Archetype archetype = this.locations[entity.Id].Archetype!;
        return archetype.IndexOfColumn(ComponentType<T>.Id) >= 0;
    }

    /// <summary>
    /// 指定した1エンティティのコンポーネントへの参照を、呼び出しのたびに列を2分探索して返す。
    /// spawn/初期化コードや単発のアドホックアクセス向けであり、フレームごとの行ループでは
    /// 使わない(そちらは<c>[GameSystem]</c>/<c>[Update]</c>経由で生成されるQueryを使う)。
    /// </summary>
    public ref T GetRef<T>(Entity entity) where T : struct
    {
        this.ValidateAlive(entity);
        ref EntityLocation location = ref this.locations[entity.Id];
        Archetype archetype = location.Archetype!;
        int columnIndex = archetype.IndexOfColumn(ComponentType<T>.Id);

        if (columnIndex < 0)
        {
            throw new InvalidOperationException($"{entity} does not have component '{typeof(T).Name}'.");
        }

        return ref archetype.GetSpan<T>(columnIndex)[location.Row];
    }

    public void Destroy(Entity entity)
    {
        this.ValidateAlive(entity);
        ref EntityLocation location = ref this.locations[entity.Id];
        Archetype archetype = location.Archetype!;

        if (archetype.RemoveRowSwapBack(location.Row, out Entity movedEntity))
        {
            this.locations[movedEntity.Id].Row = location.Row;
        }

        location.Archetype = null;
        this.generations[entity.Id]++;
        this.freeIds.Push(entity.Id);
    }

    internal void AddComponentDefault(Entity entity, int componentTypeId)
    {
        ref EntityLocation location = ref this.locations[entity.Id];
        Archetype source = location.Archetype!;

        if (source.IndexOfColumn(componentTypeId) >= 0)
        {
            return;
        }

        Archetype destination = this.GetArchetypeWithAdded(source, componentTypeId);
        this.MoveEntity(entity, source, destination);
    }

    internal void AddComponentValue<T>(Entity entity, int componentTypeId, T value) where T : struct
    {
        ref EntityLocation location = ref this.locations[entity.Id];
        Archetype source = location.Archetype!;
        int existingColumn = source.IndexOfColumn(componentTypeId);

        if (existingColumn >= 0)
        {
            source.GetSpan<T>(existingColumn)[location.Row] = value;
            return;
        }

        Archetype destination = this.GetArchetypeWithAdded(source, componentTypeId);
        this.MoveEntity(entity, source, destination);

        ref EntityLocation newLocation = ref this.locations[entity.Id];
        int newColumn = destination.IndexOfColumn(componentTypeId);
        destination.GetSpan<T>(newColumn)[newLocation.Row] = value;
    }

    internal void RemoveComponent(Entity entity, int componentTypeId)
    {
        ref EntityLocation location = ref this.locations[entity.Id];
        Archetype source = location.Archetype!;

        if (source.IndexOfColumn(componentTypeId) < 0)
        {
            return;
        }

        Archetype destination = this.GetArchetypeWithRemoved(source, componentTypeId);
        this.MoveEntity(entity, source, destination);
    }

    private Archetype GetArchetypeWithAdded(Archetype source, int componentTypeId)
    {
        if (source.TryGetAddEdge(componentTypeId, out Archetype? cached))
        {
            return cached;
        }

        int[] newIds = InsertSorted(source.ComponentTypeIds, componentTypeId);
        Archetype destination = this.GetOrCreateArchetype(newIds);
        source.SetAddEdge(componentTypeId, destination);
        return destination;
    }

    private Archetype GetArchetypeWithRemoved(Archetype source, int componentTypeId)
    {
        if (source.TryGetRemoveEdge(componentTypeId, out Archetype? cached))
        {
            return cached;
        }

        int[] newIds = RemoveSorted(source.ComponentTypeIds, componentTypeId);
        Archetype destination = this.GetOrCreateArchetype(newIds);
        source.SetRemoveEdge(componentTypeId, destination);
        return destination;
    }

    private void MoveEntity(Entity entity, Archetype source, Archetype destination)
    {
        if (ReferenceEquals(source, destination))
        {
            return;
        }

        ref EntityLocation location = ref this.locations[entity.Id];
        int sourceRow = location.Row;
        int destinationRow = destination.AddRow(entity);

        CopySharedColumns(source, sourceRow, destination, destinationRow);

        if (source.RemoveRowSwapBack(sourceRow, out Entity movedEntity))
        {
            this.locations[movedEntity.Id].Row = sourceRow;
        }

        location.Archetype = destination;
        location.Row = destinationRow;
    }

    /// <summary>
    /// 両方のArchetypeに存在する列だけを1行分コピーし、移動先にのみ存在する(新規追加された)列は
    /// 既定値にする。両方の<see cref="Archetype.ComponentTypeIds"/>が昇順ソート済みであることを
    /// 前提に、マージ走査でO(k)で解決する(辞書検索・Reflectionなし)。
    /// </summary>
    private static void CopySharedColumns(Archetype source, int sourceRow, Archetype destination, int destinationRow)
    {
        int[] sourceIds = source.ComponentTypeIds;
        int[] destinationIds = destination.ComponentTypeIds;
        int i = 0;

        for (int j = 0; j < destinationIds.Length; j++)
        {
            while (i < sourceIds.Length && sourceIds[i] < destinationIds[j])
            {
                i++;
            }

            if (i < sourceIds.Length && sourceIds[i] == destinationIds[j])
            {
                destination.GetColumn(j).CopyRowFrom(source.GetColumn(i), sourceRow, destinationRow);
                i++;
            }
            else
            {
                destination.GetColumn(j).SetDefault(destinationRow);
            }
        }
    }

    private Archetype GetOrCreateArchetype(int[] sortedComponentTypeIds)
    {
        ComponentTypeSet signature = new(sortedComponentTypeIds);

        if (this.archetypesBySignature.TryGetValue(signature, out Archetype? existing))
        {
            return existing;
        }

        IComponentColumn[] columns = new IComponentColumn[sortedComponentTypeIds.Length];

        for (int i = 0; i < sortedComponentTypeIds.Length; i++)
        {
            columns[i] = ComponentTypeRegistry.CreateColumn(sortedComponentTypeIds[i]);
        }

        Archetype archetype = new(this.archetypes.Count, sortedComponentTypeIds, columns);
        this.archetypes.Add(archetype);
        this.archetypesBySignature.Add(signature, archetype);
        return archetype;
    }

    private static int[] SortedDistinct(Span<int> ids)
    {
        ids.Sort();
        int writeIndex = 0;

        for (int readIndex = 0; readIndex < ids.Length; readIndex++)
        {
            if (readIndex == 0 || ids[readIndex] != ids[readIndex - 1])
            {
                ids[writeIndex++] = ids[readIndex];
            }
        }

        return ids[..writeIndex].ToArray();
    }

    private static int[] InsertSorted(int[] sortedIds, int id)
    {
        int[] result = new int[sortedIds.Length + 1];
        int insertAt = 0;

        while (insertAt < sortedIds.Length && sortedIds[insertAt] < id)
        {
            result[insertAt] = sortedIds[insertAt];
            insertAt++;
        }

        result[insertAt] = id;
        Array.Copy(sortedIds, insertAt, result, insertAt + 1, sortedIds.Length - insertAt);
        return result;
    }

    private static int[] RemoveSorted(int[] sortedIds, int id)
    {
        int[] result = new int[sortedIds.Length - 1];
        int writeIndex = 0;

        foreach (int existing in sortedIds)
        {
            if (existing != id)
            {
                result[writeIndex++] = existing;
            }
        }

        return result;
    }

    private int AllocateId()
    {
        if (this.freeIds.Count > 0)
        {
            return this.freeIds.Pop();
        }

        int id = this.nextId++;

        if (id >= this.generations.Length)
        {
            int newCapacity = Math.Max(4, this.generations.Length * 2);
            Array.Resize(ref this.generations, newCapacity);
            Array.Resize(ref this.locations, newCapacity);
        }

        return id;
    }

    private void ValidateAlive(Entity entity)
    {
        if (!this.IsAlive(entity))
        {
            throw new InvalidOperationException($"{entity} is not alive.");
        }
    }
}
