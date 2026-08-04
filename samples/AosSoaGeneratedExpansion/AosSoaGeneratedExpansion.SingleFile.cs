// AosSoaGeneratedExpansion.SingleFile.cs
//
// Standard single-source C# file.
// Put this file into a .NET 10 console project and compile with:
//
//   <TargetFramework>net10.0</TargetFramework>
//   <LangVersion>latest</LangVersion>
//
// This is a buildable "expanded result" example.
//
// A real Source Generator cannot be implemented and consumed by the same
// compilation. Therefore this file contains:
//
//   1. USER CODE
//   2. CODE THAT THE SG WOULD GENERATE
//   3. AoS and SoA generated storage backends
//
// Change only EntityGenerationSettings.Kind and rebuild.
// The attribute receives that const value. A real SG reads the constant and
// emits only the selected backend. This single-file example compiles both
// backends so the complete expansion can be inspected in one file.

using System;
using System.Collections.Generic;
using System.Numerics;

#nullable enable

// ============================================================================
// SHARED GENERATOR CONTRACT
// ============================================================================

public enum EntityStorageKind
{
    /// <summary>
    /// Array of Structs:
    ///
    /// CharacterData[]
    ///   [0] Transform, Velocity, Health
    ///   [1] Transform, Velocity, Health
    /// </summary>
    ObjectOrientedAos,

    /// <summary>
    /// Struct of Arrays in archetype chunks:
    ///
    /// LocalTransform[]
    /// Velocity[]
    /// Health[]
    /// </summary>
    EcsSoa
}

[AttributeUsage(
    AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class GenerateEntityAttribute : Attribute
{
    public GenerateEntityAttribute(EntityStorageKind kind)
    {
        Kind = kind;
    }

    public EntityStorageKind Kind { get; }
}

/// <summary>
/// Change this one const to switch every type that uses
/// [GenerateEntity(EntityGenerationSettings.Kind)].
///
/// Because it is a const enum value, it is legal as an attribute argument.
/// A real SG reads the resolved constant through Roslyn's SemanticModel.
/// </summary>
public static class EntityGenerationSettings
{
    // AoS:
    // public const EntityStorageKind Kind =
    //     EntityStorageKind.ObjectOrientedAos;

    // ECS / SoA:
    public const EntityStorageKind Kind =
        EntityStorageKind.EcsSoa;
}

// ============================================================================
// USER-WRITTEN COMPONENTS
// ============================================================================

public struct LocalTransform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public float Scale;

    public static LocalTransform Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale = 1f
    };
}

public struct Velocity
{
    public Vector3 Value;
}

public struct Health
{
    public int Value;
}

// ============================================================================
// USER-WRITTEN ENTITY DECLARATION
// ============================================================================

/// <summary>
/// The user writes one object-oriented partial struct.
///
/// The partial properties declare logical components. They do not decide
/// whether the physical storage is AoS or SoA.
///
/// Normal instance and static methods are handwritten domain behavior.
/// They remain unchanged when storage changes.
/// </summary>
[GenerateEntity(EntityGenerationSettings.Kind)]
public partial struct Character
{
    // SG input:
    // non-static partial property => one component declaration.
    public partial LocalTransform Transform { get; set; }

    public partial Velocity Velocity { get; set; }

    public partial Health Health { get; set; }

    /// <summary>
    /// Handwritten object-oriented behavior.
    ///
    /// AoS:
    /// TransformRef points into CharacterData[row].Transform.
    ///
    /// SoA:
    /// TransformRef points into chunk.Transforms[row].
    /// </summary>
    public void Move(float deltaTime)
    {
        ref var transform = ref TransformRef;
        ref var velocity = ref VelocityRef;

        transform.Position += velocity.Value * deltaTime;
    }

    /// <summary>
    /// Handwritten static factory.
    ///
    /// CharacterStore.Create has the same API for both generated layouts.
    /// </summary>
    public static Character Spawn(
        CharacterStore store,
        Vector3 position,
        Vector3 velocity,
        int initialHealth = 100)
    {
        ArgumentNullException.ThrowIfNull(store);

        return store.Create(new CharacterCreateInfo
        {
            Transform = new LocalTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = 1f
            },
            Velocity = new Velocity
            {
                Value = velocity
            },
            Health = new Health
            {
                Value = initialHealth
            }
        });
    }

    /// <summary>
    /// Handwritten domain operation.
    /// </summary>
    public static void ApplyDamage(
        Character target,
        int damage)
    {
        ref var health = ref target.HealthRef;
        health.Value = Math.Max(0, health.Value - Math.Max(0, damage));
    }

    public static bool IsDead(Character target)
    {
        return target.Health.Value <= 0;
    }
}

// ============================================================================
// SG-GENERATED COMMON TYPES
// ============================================================================

/// <summary>
/// Stable identity handle.
///
/// EntityId.Index indexes identity/location tables, not component arrays.
/// Generation invalidates stale handles after an index is recycled.
/// </summary>
public readonly record struct EntityId(
    int Index,
    uint Generation)
{
    public override string ToString()
    {
        return $"{Index}:{Generation}";
    }
}

/// <summary>
/// Initial values are independent from the physical storage layout.
///
/// AoS:
/// values are copied into one CharacterData row.
///
/// SoA:
/// values are distributed into separate component columns.
/// </summary>
public struct CharacterCreateInfo
{
    public LocalTransform Transform;
    public Velocity Velocity;
    public Health Health;
}

/// <summary>
/// SG-generated normalized metadata.
///
/// The real generator sorts by a stable ComponentTypeId. It must not use:
///
/// - source property order
/// - Type.GetHashCode()
/// - RuntimeTypeHandle.GetHashCode()
/// - reflection enumeration order
/// </summary>
internal static class CharacterGeneratedMetadata
{
    private static readonly string[] normalizedSignatureBacking =
    [
        nameof(Health),
        nameof(LocalTransform),
        nameof(Velocity)
    ];

    internal static ReadOnlySpan<string> NormalizedSignature =>
        normalizedSignatureBacking;
}

/// <summary>
/// Public storage facade generated for Character.
///
/// The public API is identical in AoS and SoA mode. Only the generated backend
/// changes. The storage choice is made once when this store is created, not in
/// each property access or each loop iteration.
///
/// In a production SG, only the selected backend and direct calls would be
/// emitted. The interface is used here solely so both expansions can coexist
/// in this one compilable file.
/// </summary>
public sealed class CharacterStore
{
    private readonly ICharacterGeneratedStorage _storage;

    public CharacterStore()
    {
        // EntityGenerationSettings.Kind is const.
        //
        // A real SG evaluates this same const from the attribute and emits only
        // one branch. This demo keeps both classes compiled for inspection.
        _storage =
            EntityGenerationSettings.Kind ==
            EntityStorageKind.ObjectOrientedAos
                ? new CharacterAosGeneratedStorage()
                : new CharacterSoaGeneratedStorage();
    }

    public EntityStorageKind Kind =>
        EntityGenerationSettings.Kind;

    public int Count =>
        _storage.Count;

    public Character Create(
        in CharacterCreateInfo source)
    {
        var entity = _storage.Create(source);
        return new Character(this, entity);
    }

    public bool IsAlive(EntityId entity)
    {
        return _storage.IsAlive(entity);
    }

    public void Destroy(EntityId entity)
    {
        _storage.Destroy(entity);
    }

    internal ref LocalTransform GetTransformRef(
        EntityId entity)
    {
        return ref _storage.GetTransformRef(entity);
    }

    internal ref Velocity GetVelocityRef(
        EntityId entity)
    {
        return ref _storage.GetVelocityRef(entity);
    }

    internal ref Health GetHealthRef(
        EntityId entity)
    {
        return ref _storage.GetHealthRef(entity);
    }

    /// <summary>
    /// Generated batch API.
    ///
    /// AoS backend loops CharacterData[].
    /// SoA backend loops contiguous Transform[] and Velocity[] columns.
    /// </summary>
    public void MoveAll(float deltaTime)
    {
        _storage.MoveAll(deltaTime);
    }

    public string DescribePhysicalLayout()
    {
        return _storage.DescribePhysicalLayout();
    }
}

// ============================================================================
// SG-GENERATED PARTIAL IMPLEMENTATION OF USER TYPE
// ============================================================================

/// <summary>
/// Generated object-oriented facade.
///
/// Character never duplicates component values. It contains only:
///
/// - CharacterStore reference
/// - stable EntityId
///
/// Therefore copying Character copies a handle in both storage modes.
/// The semantics do not unexpectedly change when the const switches layout.
/// </summary>
public partial struct Character :
    IEquatable<Character>
{
    private readonly CharacterStore _store;
    private readonly EntityId _entity;

    internal Character(
        CharacterStore store,
        EntityId entity)
    {
        _store = store;
        _entity = entity;
    }

    public EntityId Entity =>
        _entity;

    public bool IsAlive =>
        _store is not null &&
        _store.IsAlive(_entity);

    // Generated implementation of the user's partial property.
    //
    // AoS:
    // CharacterData[row].Transform
    //
    // SoA:
    // chunk.Transforms[row]
    public partial LocalTransform Transform
    {
        get => _store.GetTransformRef(_entity);
        set => _store.GetTransformRef(_entity) = value;
    }

    public partial Velocity Velocity
    {
        get => _store.GetVelocityRef(_entity);
        set => _store.GetVelocityRef(_entity) = value;
    }

    public partial Health Health
    {
        get => _store.GetHealthRef(_entity);
        set => _store.GetHealthRef(_entity) = value;
    }

    // Generated ref accessors avoid read-modify-write copies for struct
    // components.
    public ref LocalTransform TransformRef =>
        ref _store.GetTransformRef(_entity);

    public ref Velocity VelocityRef =>
        ref _store.GetVelocityRef(_entity);

    public ref Health HealthRef =>
        ref _store.GetHealthRef(_entity);

    public void Destroy()
    {
        _store.Destroy(_entity);
    }

    public bool Equals(Character other)
    {
        return ReferenceEquals(_store, other._store) &&
               _entity == other._entity;
    }

    public override bool Equals(object? obj)
    {
        return obj is Character other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_store, _entity);
    }

    public static bool operator ==(
        Character left,
        Character right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        Character left,
        Character right)
    {
        return !left.Equals(right);
    }
}

// ============================================================================
// ONE-FILE DEMO-ONLY STORAGE CONTRACT
// ============================================================================

/// <summary>
/// This interface is not required by the intended production output.
///
/// A production SG can emit direct concrete calls because it emits only one
/// selected backend. It exists here only to keep the AoS and SoA expansions in
/// the same compilable source file.
/// </summary>
internal interface ICharacterGeneratedStorage
{
    int Count { get; }

    EntityId Create(in CharacterCreateInfo source);

    bool IsAlive(EntityId entity);

    void Destroy(EntityId entity);

    ref LocalTransform GetTransformRef(EntityId entity);

    ref Velocity GetVelocityRef(EntityId entity);

    ref Health GetHealthRef(EntityId entity);

    void MoveAll(float deltaTime);

    string DescribePhysicalLayout();
}

// ============================================================================
// SG-GENERATED COMMON IDENTITY POOL
// ============================================================================

internal sealed class GeneratedEntityPool
{
    private readonly List<uint> _generations = [];
    private readonly List<bool> _alive = [];
    private readonly Stack<int> _freeIndices = [];

    internal EntityId Allocate()
    {
        if (_freeIndices.TryPop(out var recycledIndex))
        {
            _alive[recycledIndex] = true;

            return new EntityId(
                recycledIndex,
                _generations[recycledIndex]);
        }

        var index = _generations.Count;

        _generations.Add(1);
        _alive.Add(true);

        return new EntityId(index, 1);
    }

    internal bool IsAlive(EntityId entity)
    {
        return (uint)entity.Index <
                   (uint)_generations.Count &&
               _alive[entity.Index] &&
               _generations[entity.Index] ==
                   entity.Generation;
    }

    internal void Validate(EntityId entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException(
                $"Entity {entity} is dead or stale.");
        }
    }

    internal void Destroy(EntityId entity)
    {
        Validate(entity);

        _alive[entity.Index] = false;

        var next =
            _generations[entity.Index] + 1;

        // Keep generation zero reserved.
        _generations[entity.Index] =
            next == 0 ? 1 : next;

        _freeIndices.Push(entity.Index);
    }
}

// ============================================================================
// SG EXPANSION A — OBJECT-ORIENTED AoS
// ============================================================================

/// <summary>
/// Generated AoS row.
///
/// Every entity's components are physically adjacent inside one struct.
/// </summary>
internal struct CharacterAosGeneratedData
{
    internal LocalTransform Transform;
    internal Velocity Velocity;
    internal Health Health;
}

/// <summary>
/// Generated AoS storage.
///
/// Physical layout:
///
/// CharacterAosGeneratedData[]
/// ┌────────────────────────────────────┐
/// │ Transform0 │ Velocity0 │ Health0   │
/// ├────────────────────────────────────┤
/// │ Transform1 │ Velocity1 │ Health1   │
/// └────────────────────────────────────┘
///
/// EntityId.Index does not index _rows directly. It indexes _locations,
/// because swap-back removal changes row positions.
/// </summary>
internal sealed class CharacterAosGeneratedStorage :
    ICharacterGeneratedStorage
{
    private readonly GeneratedEntityPool _entityPool =
        new();

    private CharacterAosGeneratedData[] _rows =
        new CharacterAosGeneratedData[4];

    private EntityId[] _rowEntities =
        new EntityId[4];

    // EntityId.Index -> current AoS row.
    private int[] _locations =
        new int[4];

    private int _count;

    public int Count =>
        _count;

    public EntityId Create(
        in CharacterCreateInfo source)
    {
        EnsureRowCapacity(_count + 1);

        var entity =
            _entityPool.Allocate();

        EnsureEntityCapacity(entity.Index + 1);

        var row = _count++;

        _rows[row] =
            new CharacterAosGeneratedData
            {
                Transform = source.Transform,
                Velocity = source.Velocity,
                Health = source.Health
            };

        _rowEntities[row] = entity;
        _locations[entity.Index] = row;

        return entity;
    }

    public bool IsAlive(EntityId entity)
    {
        return _entityPool.IsAlive(entity);
    }

    public void Destroy(EntityId entity)
    {
        _entityPool.Validate(entity);

        var removedRow =
            _locations[entity.Index];

        var lastRow =
            _count - 1;

        if (removedRow != lastRow)
        {
            // Keep rows dense by moving the final row into the hole.
            _rows[removedRow] =
                _rows[lastRow];

            var movedEntity =
                _rowEntities[lastRow];

            _rowEntities[removedRow] =
                movedEntity;

            // Only the moved entity's location changes.
            _locations[movedEntity.Index] =
                removedRow;
        }

        _rows[lastRow] = default;
        _rowEntities[lastRow] = default;
        _count--;

        _entityPool.Destroy(entity);
    }

    public ref LocalTransform GetTransformRef(
        EntityId entity)
    {
        var row = ResolveRow(entity);
        return ref _rows[row].Transform;
    }

    public ref Velocity GetVelocityRef(
        EntityId entity)
    {
        var row = ResolveRow(entity);
        return ref _rows[row].Velocity;
    }

    public ref Health GetHealthRef(
        EntityId entity)
    {
        var row = ResolveRow(entity);
        return ref _rows[row].Health;
    }

    public void MoveAll(float deltaTime)
    {
        // AoS batch path:
        // each iteration addresses one complete entity row.
        for (var row = 0; row < _count; row++)
        {
            _rows[row].Transform.Position +=
                _rows[row].Velocity.Value *
                deltaTime;
        }
    }

    public string DescribePhysicalLayout()
    {
        return
            "AoS: CharacterAosGeneratedData[]; " +
            "each row contains Transform, Velocity, Health.";
    }

    private int ResolveRow(EntityId entity)
    {
        _entityPool.Validate(entity);
        return _locations[entity.Index];
    }

    private void EnsureRowCapacity(int required)
    {
        if (required <= _rows.Length)
        {
            return;
        }

        var capacity =
            Math.Max(required, _rows.Length * 2);

        Array.Resize(ref _rows, capacity);
        Array.Resize(ref _rowEntities, capacity);
    }

    private void EnsureEntityCapacity(int required)
    {
        if (required <= _locations.Length)
        {
            return;
        }

        var capacity =
            Math.Max(required, _locations.Length * 2);

        Array.Resize(ref _locations, capacity);
    }
}

// ============================================================================
// SG EXPANSION B — ECS / SoA ARCHETYPE CHUNKS
// ============================================================================

/// <summary>
/// Generated chunk for the normalized component set:
///
/// { Health, LocalTransform, Velocity }
///
/// Physical layout:
///
/// EntityId[]
/// LocalTransform[]
/// Velocity[]
/// Health[]
///
/// Every row index identifies one entity inside this chunk.
/// </summary>
internal sealed class CharacterSoaGeneratedChunk
{
    internal readonly EntityId[] Entities;
    internal readonly LocalTransform[] Transforms;
    internal readonly Velocity[] Velocities;
    internal readonly Health[] Health;

    internal int Count;

    internal CharacterSoaGeneratedChunk(
        int capacity)
    {
        Entities =
            new EntityId[capacity];

        Transforms =
            new LocalTransform[capacity];

        Velocities =
            new Velocity[capacity];

        Health =
            new Health[capacity];
    }

    internal bool HasCapacity =>
        Count < Entities.Length;

    internal int Add(
        EntityId entity,
        in CharacterCreateInfo source)
    {
        if (!HasCapacity)
        {
            throw new InvalidOperationException(
                "The generated chunk is full.");
        }

        var row = Count++;

        Entities[row] = entity;
        Transforms[row] = source.Transform;
        Velocities[row] = source.Velocity;
        Health[row] = source.Health;

        return row;
    }

    /// <summary>
    /// Remove by moving the final row into the removed row.
    ///
    /// Returns the entity that moved, or null when the removed row was already
    /// the final row.
    /// </summary>
    internal EntityId? RemoveSwapBack(int row)
    {
        var lastRow =
            Count - 1;

        EntityId? movedEntity =
            null;

        if (row != lastRow)
        {
            movedEntity =
                Entities[lastRow];

            Entities[row] =
                Entities[lastRow];

            Transforms[row] =
                Transforms[lastRow];

            Velocities[row] =
                Velocities[lastRow];

            Health[row] =
                Health[lastRow];
        }

        Entities[lastRow] = default;
        Transforms[lastRow] = default;
        Velocities[lastRow] = default;
        Health[lastRow] = default;

        Count--;

        return movedEntity;
    }
}

internal readonly record struct CharacterSoaGeneratedLocation(
    int ChunkIndex,
    int Row);

/// <summary>
/// Generated SoA archetype storage.
///
/// EntityId.Index indexes _locations, not a component column.
///
/// _locations[entity.Index]
///     -> chunk index
///     -> row
///     -> Transforms[row], Velocities[row], Health[row]
///
/// When an entity changes archetype, its EntityId remains stable and only its
/// location-table entry changes.
/// </summary>
internal sealed class CharacterSoaGeneratedStorage :
    ICharacterGeneratedStorage
{
    private const int ChunkCapacity = 4;

    private readonly GeneratedEntityPool _entityPool =
        new();

    private readonly List<CharacterSoaGeneratedChunk> _chunks =
        [];

    private CharacterSoaGeneratedLocation[] _locations =
        new CharacterSoaGeneratedLocation[4];

    private int _count;

    public int Count =>
        _count;

    public EntityId Create(
        in CharacterCreateInfo source)
    {
        var chunkIndex =
            GetOrCreateWritableChunkIndex();

        var chunk =
            _chunks[chunkIndex];

        var entity =
            _entityPool.Allocate();

        EnsureEntityCapacity(entity.Index + 1);

        var row =
            chunk.Add(entity, source);

        _locations[entity.Index] =
            new CharacterSoaGeneratedLocation(
                chunkIndex,
                row);

        _count++;

        return entity;
    }

    public bool IsAlive(EntityId entity)
    {
        return _entityPool.IsAlive(entity);
    }

    public void Destroy(EntityId entity)
    {
        var location =
            Resolve(entity);

        var chunk =
            _chunks[location.ChunkIndex];

        var movedEntity =
            chunk.RemoveSwapBack(location.Row);

        if (movedEntity is EntityId moved)
        {
            // The moved entity keeps its ID. Only its row changes.
            _locations[moved.Index] =
                new CharacterSoaGeneratedLocation(
                    location.ChunkIndex,
                    location.Row);
        }

        _count--;

        _entityPool.Destroy(entity);
    }

    public ref LocalTransform GetTransformRef(
        EntityId entity)
    {
        var location =
            Resolve(entity);

        return ref _chunks[location.ChunkIndex]
            .Transforms[location.Row];
    }

    public ref Velocity GetVelocityRef(
        EntityId entity)
    {
        var location =
            Resolve(entity);

        return ref _chunks[location.ChunkIndex]
            .Velocities[location.Row];
    }

    public ref Health GetHealthRef(
        EntityId entity)
    {
        var location =
            Resolve(entity);

        return ref _chunks[location.ChunkIndex]
            .Health[location.Row];
    }

    public void MoveAll(float deltaTime)
    {
        // SoA hot path:
        // no per-entity location lookup and no object facade construction.
        //
        // Each chunk exposes contiguous component columns.
        foreach (var chunk in _chunks)
        {
            var transforms =
                chunk.Transforms;

            var velocities =
                chunk.Velocities;

            for (var row = 0;
                 row < chunk.Count;
                 row++)
            {
                transforms[row].Position +=
                    velocities[row].Value *
                    deltaTime;
            }
        }
    }

    public string DescribePhysicalLayout()
    {
        return
            "SoA: archetype chunks with EntityId[], " +
            "LocalTransform[], Velocity[], Health[] columns.";
    }

    private CharacterSoaGeneratedLocation Resolve(
        EntityId entity)
    {
        _entityPool.Validate(entity);
        return _locations[entity.Index];
    }

    private int GetOrCreateWritableChunkIndex()
    {
        if (_chunks.Count > 0 &&
            _chunks[^1].HasCapacity)
        {
            return _chunks.Count - 1;
        }

        _chunks.Add(
            new CharacterSoaGeneratedChunk(
                ChunkCapacity));

        return _chunks.Count - 1;
    }

    private void EnsureEntityCapacity(int required)
    {
        if (required <= _locations.Length)
        {
            return;
        }

        var capacity =
            Math.Max(
                required,
                _locations.Length * 2);

        Array.Resize(
            ref _locations,
            capacity);
    }
}

// ============================================================================
// OPTIONAL CONSOLE ENTRY POINT
//
// The file compiles as a normal console Program.cs.
// Running it is not required to inspect the generated expansion.
// ============================================================================

public static class サンプル
{
    public static void 呼び出しサンプル()
    {
        var store =
            new CharacterStore();

        var player =
            Character.Spawn(
                store,
                position: Vector3.Zero,
                velocity: Vector3.UnitX);

        var enemy =
            Character.Spawn(
                store,
                position: new Vector3(10f, 0f, 0f),
                velocity: -Vector3.UnitX,
                initialHealth: 50);

        player.Move(0.5f);
        Character.ApplyDamage(enemy, 20);

        store.MoveAll(1f);

        Console.WriteLine(
            $"Kind: {store.Kind}");

        Console.WriteLine(
            store.DescribePhysicalLayout());

        Console.WriteLine(
            $"Signature: {string.Join(
                ", ",
                CharacterGeneratedMetadata
                    .NormalizedSignature
                    .ToArray())}");

        Console.WriteLine(
            $"Player position: " +
            $"{player.Transform.Position}");

        Console.WriteLine(
            $"Enemy health: " +
            $"{enemy.Health.Value}");
    }
}
