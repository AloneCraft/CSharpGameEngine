namespace CSharpGameEngine.Entities;

/// <summary>
/// <see cref="EntityQuery.Update"/>が返す、現在マッチしているArchetypeとその列インデックスの
/// スナップショット。列インデックスの2分探索は<see cref="EntityQuery"/>が新Archetypeを発見した
/// 瞬間に1回だけ行われており、<see cref="ColumnAt"/>ではflat配列の添字読みだけが発生する。
/// </summary>
public readonly struct QueryMatches
{
    private readonly Archetype[] archetypes;
    private readonly int[] columns;
    private readonly int stride;

    internal QueryMatches(Archetype[] archetypes, int[] columns, int stride, int count)
    {
        this.archetypes = archetypes;
        this.columns = columns;
        this.stride = stride;
        this.Count = count;
    }

    public int Count { get; }

    public Archetype ArchetypeAt(int matchIndex)
    {
        return this.archetypes[matchIndex];
    }

    public int ColumnAt(int matchIndex, int accessIndex)
    {
        return this.columns[(matchIndex * this.stride) + accessIndex];
    }
}
