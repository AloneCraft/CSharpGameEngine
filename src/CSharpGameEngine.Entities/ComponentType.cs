namespace CSharpGameEngine.Entities;

/// <summary>
/// コンポーネント型<typeparamref name="T"/>ごとの実行時Id。静的初期化子は型ごとにプロセス内で
/// ちょうど1回だけ実行されるため、<see cref="ComponentTypeRegistry.Register"/>内の辞書検索は
/// コンポーネント型ごとに1回だけ発生し、フレームごと・エンティティごと・アーキタイプごとには
/// 発生しない。これが「実行時に動的生成されるArchetype」と「ホットループでReflectionを使わない」
/// を両立させる鍵(Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md参照)。
/// </summary>
public static class ComponentType<T> where T : struct
{
    public static readonly int Id = ComponentTypeRegistry.Register(typeof(T), static () => new ComponentColumn<T>());
}

/// <summary>
/// <see cref="ComponentType{T}.Id"/>が実行時に登録する、型からId・Idから列ファクトリへの対応表。
/// Idの値はプロセスローカルであり静的初期化順に依存するため、永続化や外部可視の契約(テストの期待値等)
/// に使ってはならない。
/// </summary>
internal static class ComponentTypeRegistry
{
    internal const int MaxComponentTypes = ComponentMask.BitCount;

    private static readonly object Gate = new();
    private static readonly Dictionary<Type, int> IdsByType = [];
    private static Func<IComponentColumn>[] factories = new Func<IComponentColumn>[32];
    private static Type[] clrTypes = new Type[32];
    private static int count;

    internal static int Register(Type clrType, Func<IComponentColumn> factory)
    {
        lock (Gate)
        {
            if (IdsByType.TryGetValue(clrType, out int existingId))
            {
                return existingId;
            }

            if (count >= MaxComponentTypes)
            {
                throw new InvalidOperationException(
                    $"Cannot register component type '{clrType}': the {MaxComponentTypes}-type limit (ComponentMask capacity) has been reached.");
            }

            if (count >= factories.Length)
            {
                Array.Resize(ref factories, factories.Length * 2);
                Array.Resize(ref clrTypes, clrTypes.Length * 2);
            }

            int id = count++;
            IdsByType[clrType] = id;
            factories[id] = factory;
            clrTypes[id] = clrType;
            return id;
        }
    }

    internal static IComponentColumn CreateColumn(int componentTypeId)
    {
        return factories[componentTypeId]();
    }

    internal static Type GetClrType(int componentTypeId)
    {
        return clrTypes[componentTypeId];
    }
}
