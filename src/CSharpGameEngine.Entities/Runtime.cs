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

    private T[][] chunks = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureCapacity(int requiredCount)
    {
        Debug.Assert(requiredCount >= 0, "CGE-ECS-UNCHECKED-001: requiredCount must be non-negative; remove this assertion after hot-path validation.");

        if (requiredCount == 0)
        {
            return;
        }

        int chunkIndex = (requiredCount - 1) >> ChunkShift;

        if (chunkIndex >= this.chunks.Length)
        {
            int length = this.chunks.Length == 0 ? 1 : this.chunks.Length;
            while (length <= chunkIndex)
            {
                length <<= 1;
            }

            Array.Resize(ref this.chunks, length);
        }

        if (this.chunks[chunkIndex] is null)
        {
            this.chunks[chunkIndex] = new T[ChunkSize];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetRefUnchecked(int index)
    {
        return ref this.chunks[index >> ChunkShift][index & ChunkMask];
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
    private readonly ChunkList<T> items = new();

    internal int Count { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(T item)
    {
        int index = this.Count;
        this.items.EnsureCapacity(index + 1);
        this.items.GetRefUnchecked(index) = item;
        this.Count = index + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetRefUnchecked(int index)
    {
        Debug.Assert((uint)index < (uint)this.Count, "CGE-ECS-UNCHECKED-002: buffer index must be valid; remove this assertion after hot-path validation.");
        return ref this.items.GetRefUnchecked(index);
    }

    internal void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            for (int index = 0; index < this.Count; index++)
            {
                this.items.ClearAtUnchecked(index);
            }
        }

        this.Count = 0;
    }
}
