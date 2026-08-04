namespace CSharpGameEngine.Ecs;

/// <summary>
/// エンティティアロケータ(Id再利用+Generation)と、正規化済みコンポーネント集合をキーにした
/// アーキタイプストレージ辞書を保持する。個々のアーキタイプの形状は<see cref="IEcsArchetype{TSelf}"/>
/// 経由でのみ扱い、World自身はどんなアーキタイプが存在するかを事前に知らない。
/// </summary>
public sealed class World
{
    private struct EntityLocation
    {
        internal ArchetypeStorage? Storage;
        internal int Row;
    }

    private readonly Dictionary<ArchetypeSignature, ArchetypeStorage> storages = [];
    private readonly Stack<int> freeIds = [];
    private int[] generations = [];
    private EntityLocation[] locations = [];
    private int nextId;

    internal int DebugArchetypeStorageCount => this.storages.Count;

    /// <summary>
    /// <typeparamref name="T"/>用のアーキタイプストレージ(既存または新規作成)にエンティティの行を割り当てる。
    /// ビュー構造体の構築は呼び出し元(<see cref="WorldEcsExtensions"/>や生成された名前付き引数ファクトリ)が行う。
    /// </summary>
    public Entity CreateEntityCore<T>() where T : struct, IEcsArchetype<T>
    {
        ArchetypeStorage storage = this.GetOrCreateStorage<T>();
        int id = this.AllocateId();
        Entity entity = new(id, this.generations[id]);
        int row = storage.AddRow(entity);
        this.locations[id] = new EntityLocation { Storage = storage, Row = row };
        return entity;
    }

    /// <summary>
    /// 指定エンティティの現在の行位置を毎回解決してからコンポーネントへの参照を返す。
    /// ビュー構造体側は行番号をキャッシュしないため、将来アーキタイプ間の構造変更に対応する余地を残す。
    /// </summary>
    public ref T GetComponentRef<T>(Entity entity, int columnIndex) where T : struct
    {
        this.ValidateAlive(entity);
        ref EntityLocation location = ref this.locations[entity.Id];
        ComponentColumn<T> column = (ComponentColumn<T>)location.Storage!.GetColumn(columnIndex);
        return ref column.Items[location.Row];
    }

    /// <summary>
    /// Appends a view for every currently-alive entity whose archetype exactly matches
    /// <typeparamref name="T"/>'s normalized component set - not a query (see
    /// <see cref="EcsReadOnlyAttribute"/>'s remarks: there is no multi-archetype matching or
    /// filtering here, only enumeration of one exact archetype's storage). If no entity of this
    /// exact archetype has ever been created, nothing is appended and no storage is allocated.
    /// </summary>
    public void CollectViews<T>(List<T> results) where T : struct, IEcsArchetype<T>
    {
        ArchetypeSignature signature = new(T.ComponentTypes);
        if (!this.storages.TryGetValue(signature, out ArchetypeStorage? storage))
        {
            return;
        }

        for (int row = 0; row < storage.Count; row++)
        {
            results.Add(T.CreateView(this, storage.GetEntity(row)));
        }
    }

    /// <summary>
    /// <typeparamref name="T"/>の正規化済みコンポーネント集合と完全一致するアーキタイプストレージを、
    /// 行ごとのオーバーヘッドがゼロのバッチビューとして取得する。<see cref="CollectViews{T}"/>と同様、
    /// 複数アーキタイプ横断のQueryではなく、1つの正規化済み集合への完全一致のみを扱う。
    /// 正規化済み集合が同じ別のEntity型が既に行を持っていれば、それらも同じChunkに含まれる
    /// (<see cref="ArchetypeSignature"/>がType[]シーケンス一致で共有ストレージを解決するため)。
    /// </summary>
    public bool TryGetArchetypeChunk<T>(out ArchetypeChunk chunk) where T : struct, IEcsArchetype<T>
    {
        ArchetypeSignature signature = new(T.ComponentTypes);

        if (this.storages.TryGetValue(signature, out ArchetypeStorage? storage))
        {
            chunk = new ArchetypeChunk(storage);
            return true;
        }

        chunk = default;
        return false;
    }

    public bool IsAlive(Entity entity)
    {
        return entity.Id >= 0
            && entity.Id < this.nextId
            && this.generations[entity.Id] == entity.Generation
            && this.locations[entity.Id].Storage is not null;
    }

    public void Destroy(Entity entity)
    {
        this.ValidateAlive(entity);
        ref EntityLocation location = ref this.locations[entity.Id];
        ArchetypeStorage storage = location.Storage!;

        if (storage.RemoveRowSwapBack(location.Row, out Entity movedEntity))
        {
            this.locations[movedEntity.Id].Row = location.Row;
        }

        location.Storage = null;
        this.generations[entity.Id]++;
        this.freeIds.Push(entity.Id);
    }

    private ArchetypeStorage GetOrCreateStorage<T>() where T : struct, IEcsArchetype<T>
    {
        ArchetypeSignature signature = new(T.ComponentTypes);

        if (!this.storages.TryGetValue(signature, out ArchetypeStorage? storage))
        {
            storage = new ArchetypeStorage(T.ComponentTypes, T.CreateColumns());
            this.storages.Add(signature, storage);
        }

        return storage;
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
