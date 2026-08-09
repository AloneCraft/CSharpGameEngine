using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.Entities;

/// <summary>
/// Systemであることを示す空のマーカーinterface。属性を一切使わず、これを実装するstructが
/// Systemとして認識される(ADR-0033)。<c>Update</c>/<c>Spawn</c>という名前のメソッドの引数から、
/// Archetype・Component・Contextがすべて構造的に推論される。
/// </summary>
public interface ISystem
{
}

/// <summary>
/// 生成された<c>ArchetypeNN</c> structが実装する空のマーカーinterface。
/// </summary>
public interface IArchetype
{
}

/// <summary>
/// Entityの一意な識別子。Generationを持たない単純な単調増加ID
/// (<c>ArchetypeNN.Create</c>が<c>World.EntityArchetypeIndices.Count</c>をそのまま割り当てる)。
/// </summary>
public record struct Entity(int Id);

/// <summary>
/// 1フレーム分の実行状態。<c>Update</c>メソッドの末尾引数として必ず<c>ref</c>で渡され、
/// <see cref="Enabled"/>をfalseにするとGeneratedGameLoop.Runのループが終了する。
/// </summary>
public partial struct State(World World)
{
    public bool Enabled { get; set; }

    public World World { get; } = World;
}

/// <summary>
/// Entityの格納先。この宣言はEntityの位置表だけを持ち、Archetypeごとのリスト・
/// <c>MoveArchetype</c>・<c>Detach</c>・<c>PlaybackMoveArchetypes</c>は
/// <c>GameSystemGenerator</c>が同じ<c>partial struct</c>へ生成する(ADR-0033決定事項1: partial型は
/// 同一コンパイル内でしか合成できないため、このファイルは各消費プロジェクトが
/// <c>&lt;Compile Include Link&gt;</c>でソースとして取り込む)。
/// </summary>
[StructLayout(LayoutKind.Auto)]
public partial struct World
{
    internal readonly ChunkList<(Entity Entity, Archetype Archetype, int Index)> EntityArchetypeIndices = new();
}

/// <summary>
/// 固定サイズチャンクで伸長する可変長配列。要素を<c>ref</c>で返せる(配列のインデクサと同じ)ため、
/// Archetypeの行をコピーせずに直接書き換えられる。
/// </summary>
public sealed class ChunkList<T>
{
    private const int ChunkSize = 4096;

    private T[][] Chunks = [];

    public int Count { get; private set; }

    public ref T this[int Index]
    {
        get
        {
            if ((uint)Index >= (uint)this.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(Index));
            }

            return ref this.Chunks[Index / ChunkSize][Index % ChunkSize];
        }
    }

    public void Add(T Item)
    {
        var ChunkIndex = this.Count / ChunkSize;
        var OffsetIndex = this.Count % ChunkSize;

        if (OffsetIndex == 0)
        {
            if (ChunkIndex == this.Chunks.Length)
            {
                var Next = new T[this.Chunks.Length == 0 ? 1 : this.Chunks.Length * 2][];
                this.Chunks.CopyTo(Next, 0);
                this.Chunks = Next;
            }

            this.Chunks[ChunkIndex] = new T[ChunkSize];
        }

        this.Chunks[ChunkIndex][OffsetIndex] = Item;
        this.Count++;
    }

    public void RemoveAt(int Index)
    {
        if ((uint)Index >= (uint)this.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(Index));
        }

        var Last = this.Count - 1;

        if (Index != Last)
        {
            this.Chunks[Index / ChunkSize][Index % ChunkSize] =
                this.Chunks[Last / ChunkSize][Last % ChunkSize];
        }

        this.Chunks[Last / ChunkSize][Last % ChunkSize] = default!;
        this.Count--;
    }

    /// <summary>末尾の要素を<paramref name="Index"/>へ詰めて削除する。詰め替えが実際に起きた場合のみtrueを返し、呼び出し側は移動した要素の位置表を修復する。</summary>
    public bool RemoveAtSwapBack(int Index, out T Moved)
    {
        if ((uint)Index >= (uint)this.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(Index));
        }

        var Last = this.Count - 1;

        if (Index != Last)
        {
            Moved = this.Chunks[Last / ChunkSize][Last % ChunkSize];
            this.Chunks[Index / ChunkSize][Index % ChunkSize] = Moved;
            this.Chunks[Last / ChunkSize][Last % ChunkSize] = default!;
            this.Count--;

            return true;
        }

        Moved = default!;
        this.Chunks[Last / ChunkSize][Last % ChunkSize] = default!;
        this.Count--;

        return false;
    }

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            for (var Index = 0; Index < this.Count; Index++)
            {
                this.Chunks[Index / ChunkSize][Index % ChunkSize] = default!;
            }
        }

        this.Count = 0;
    }
}
