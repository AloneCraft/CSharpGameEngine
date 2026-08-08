namespace CSharpGameEngine.Entities;

/// <summary>
/// 構造変更(Component追加・削除・Entity生成・破棄)を記録し、<see cref="Playback"/>で安全な地点
/// (既定では生成された<c>GeneratedGameLoop.Update</c>の末尾)にまとめて適用する。記録した順序
/// (FIFO)どおりに適用する - 同一Entityへの<c>Add&lt;T&gt;</c>のあと<c>Remove&lt;T&gt;</c>を積めば
/// 正味Removeになる。既に持つ型への<c>Add&lt;T&gt;(entity)</c>(値なし)は何もしない
/// (既存値を潰さない)。既に持つ型への<c>Add&lt;T&gt;(entity,value)</c>は値のみ上書きする。
/// 持たない型への<c>Remove&lt;T&gt;</c>と、死んでいるEntityへの操作はno-op(例外を投げない)。
/// </summary>
public sealed class EntityCommandBuffer
{
    private enum CommandKind : byte
    {
        CreateEntity,
        AddDefault,
        AddValue,
        Remove,
        Destroy,
    }

    private readonly struct Command
    {
        internal readonly CommandKind Kind;
        internal readonly Entity Entity;
        internal readonly int ComponentTypeId;
        internal readonly int Payload;

        internal Command(CommandKind kind, Entity entity, int componentTypeId, int payload)
        {
            this.Kind = kind;
            this.Entity = entity;
            this.ComponentTypeId = componentTypeId;
            this.Payload = payload;
        }
    }

    private readonly List<Command> commands = [];
    private readonly List<EntityArchetype> pendingArchetypes = [];
    private readonly List<Entity> createdEntities = [];
    private IComponentValueStore?[] valueStores = new IComponentValueStore?[32];

    public void Add<T>(Entity entity) where T : struct
    {
        this.commands.Add(new Command(CommandKind.AddDefault, entity, ComponentType<T>.Id, -1));
    }

    public void Add<T>(Entity entity, in T value) where T : struct
    {
        int componentTypeId = ComponentType<T>.Id;
        ComponentValueStore<T> store = this.GetOrCreateStore<T>(componentTypeId);
        int valueIndex = store.Add(in value);
        this.commands.Add(new Command(CommandKind.AddValue, entity, componentTypeId, valueIndex));
    }

    public void Remove<T>(Entity entity) where T : struct
    {
        this.commands.Add(new Command(CommandKind.Remove, entity, ComponentType<T>.Id, -1));
    }

    public void Destroy(Entity entity)
    {
        this.commands.Add(new Command(CommandKind.Destroy, entity, -1, -1));
    }

    /// <summary>
    /// 一時ハンドル(負のId、<see cref="Playback"/>までWorldには存在しない)を返す。同じ
    /// バッファ内で以降のコマンドの<paramref name="archetype"/>とは別のEntity引数として渡せる。
    /// </summary>
    public Entity CreateEntity(EntityArchetype archetype)
    {
        int pendingIndex = this.pendingArchetypes.Count;
        this.pendingArchetypes.Add(archetype);
        Entity temporary = Entity.Temporary(pendingIndex);
        this.commands.Add(new Command(CommandKind.CreateEntity, temporary, -1, pendingIndex));
        return temporary;
    }

    public void Playback(World world)
    {
        foreach (Command command in this.commands)
        {
            if (command.Kind == CommandKind.CreateEntity)
            {
                this.createdEntities.Add(world.CreateEntity(this.pendingArchetypes[command.Payload]));
                continue;
            }

            Entity entity = this.Resolve(command.Entity);

            if (!world.IsAlive(entity))
            {
                continue;
            }

            switch (command.Kind)
            {
                case CommandKind.AddDefault:
                    world.AddComponentDefault(entity, command.ComponentTypeId);
                    break;
                case CommandKind.AddValue:
                    this.valueStores[command.ComponentTypeId]!.WriteValue(world, entity, command.ComponentTypeId, command.Payload);
                    break;
                case CommandKind.Remove:
                    world.RemoveComponent(entity, command.ComponentTypeId);
                    break;
                case CommandKind.Destroy:
                    world.Destroy(entity);
                    break;
            }
        }

        this.Reset();
    }

    private Entity Resolve(Entity entity)
    {
        return entity.IsTemporary ? this.createdEntities[entity.TemporaryIndex] : entity;
    }

    private ComponentValueStore<T> GetOrCreateStore<T>(int componentTypeId) where T : struct
    {
        if (componentTypeId >= this.valueStores.Length)
        {
            Array.Resize(ref this.valueStores, Math.Max(componentTypeId + 1, this.valueStores.Length * 2));
        }

        if (this.valueStores[componentTypeId] is not ComponentValueStore<T> store)
        {
            store = new ComponentValueStore<T>();
            this.valueStores[componentTypeId] = store;
        }

        return store;
    }

    private void Reset()
    {
        this.commands.Clear();
        this.pendingArchetypes.Clear();
        this.createdEntities.Clear();

        foreach (IComponentValueStore? store in this.valueStores)
        {
            store?.Clear();
        }
    }
}
