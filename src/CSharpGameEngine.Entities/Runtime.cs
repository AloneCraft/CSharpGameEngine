using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.Entities;

public interface ISystem
{
}

public interface IArchetype
{
}

public record struct Entity(int Id);

public partial struct State(World World)
{
    public bool Enabled { get; set; }

    public World World { get; } = World;
}

[StructLayout(LayoutKind.Auto)]
public partial struct World
{
    internal readonly ChunkBuffer<(Entity Entity, Archetype Archetype, int Index)> EntityArchetypeIndices = new();
}

/// <summary>SoA列の物理ストレージ。論理長は所有者が管理する。</summary>
public sealed class ChunkList<T>
{
    private const int ChunkShift = 12;
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    private T[][] Chunks = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureCapacity(int RequiredCount)
    {
        Debug.Assert(RequiredCount >= 0, "CGE-ECS-UNCHECKED-001: requiredCount must be non-negative; remove this assertion after hot-path validation.");

        if (RequiredCount == 0)
        {
            return;
        }

        int ChunkIndex = (RequiredCount - 1) >> ChunkShift;

        if (ChunkIndex >= this.Chunks.Length)
        {
            int Length = this.Chunks.Length == 0 ? 1 : this.Chunks.Length;
            while (Length <= ChunkIndex)
            {
                Length <<= 1;
            }

            Array.Resize(ref this.Chunks, Length);
        }

        if (this.Chunks[ChunkIndex] is null)
        {
            this.Chunks[ChunkIndex] = new T[ChunkSize];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetRefUnchecked(int index)
    {
        return ref this.Chunks[index >> ChunkShift][index & ChunkMask];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ClearAtUnchecked(int index)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            this.GetRefUnchecked(index) = default!;
        }
    }
}

/// <summary>位置表・構造変更キュー用の論理長を持つ内部バッファ。</summary>
internal sealed class ChunkBuffer<T>
{
    private readonly ChunkList<T> Items = new();

    internal int Count { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(T Item)
    {
        int index = this.Count;
        this.Items.EnsureCapacity(index + 1);
        this.Items.GetRefUnchecked(index) = Item;
        this.Count = index + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetRefUnchecked(int index)
    {
        Debug.Assert((uint)index < (uint)this.Count, "CGE-ECS-UNCHECKED-002: buffer index must be valid; remove this assertion after hot-path validation.");
        return ref this.Items.GetRefUnchecked(index);
    }

    internal void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            for (int index = 0; index < this.Count; index++)
            {
                this.Items.ClearAtUnchecked(index);
            }
        }

        this.Count = 0;
    }
}
