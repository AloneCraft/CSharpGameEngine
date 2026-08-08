using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal partial struct State(World World){
    public         bool   Enabled   { get; set; }
    public         World  World     { get; } = World;
}
[StructLayout(LayoutKind.Auto)]
internal partial struct World{
    internal readonly ChunkList<(Entity Entity,Archetype Archetype,int Index)> EntityArchetypeIndices =new();
}
internal record struct Entity(int Id);
internal interface IArchetype{
}
internal interface ISystem{
}
internal sealed class ChunkList<T>{
    private const int ChunkSize = 4096;
    private T[][] Chunks = [];
    public int Count{get; private set; }
    public ref T this[int Index]{
        get{
            if ((uint)Index >= (uint)this.Count) throw new ArgumentOutOfRangeException(nameof(Index));
            return ref this.Chunks[Index / ChunkSize][Index % ChunkSize];
        }
    }
    public void Add(T Item){
        var ChunkIndex  = this.Count / ChunkSize;
        var OffsetIndex = this.Count % ChunkSize;
        if (OffsetIndex == 0){
            if (ChunkIndex == this.Chunks.Length){
                var Next = new T[this.Chunks.Length == 0 ? 1 : this.Chunks.Length * 2][];
                this.Chunks.CopyTo(Next, 0);
                this.Chunks = Next;
            }
            this.Chunks[ChunkIndex] = new T[ChunkSize];
        }
        this.Chunks[ChunkIndex][OffsetIndex] = Item;
        this.Count++;
    }
    public void RemoveAt(int Index){
        if ((uint)Index >= (uint)this.Count) throw new ArgumentOutOfRangeException(nameof(Index));
        var Last = this.Count - 1;
        if (Index != Last){
            this.Chunks[Index    / ChunkSize][Index % ChunkSize] =
                this.Chunks[Last / ChunkSize][Last  % ChunkSize];
        }
        this.Chunks[Last / ChunkSize][Last % ChunkSize] = default!;
        this.Count--;
    }
    public bool RemoveAtSwapBack(int Index, out T Moved){
        if ((uint)Index >= (uint)this.Count)
            throw new ArgumentOutOfRangeException(nameof(Index));
        var Last = this.Count - 1;
        if (Index != Last){
            Moved = this.Chunks[Last / ChunkSize][Last  % ChunkSize];
            this.Chunks[Index        / ChunkSize][Index % ChunkSize] = Moved;
            this.Chunks[Last / ChunkSize][Last % ChunkSize] = default!;
            this.Count--;
            return true;
        }
        Moved                                       = default!;
        this.Chunks[Last / ChunkSize][Last % ChunkSize] = default!;
        this.Count--;
        return false;
    }
    public void Clear(){
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()){
            for (var Index = 0; Index < this.Count; Index++){
                this.Chunks[Index / ChunkSize][Index % ChunkSize] = default!;
            }
        }
        this.Count = 0;
    }
}
