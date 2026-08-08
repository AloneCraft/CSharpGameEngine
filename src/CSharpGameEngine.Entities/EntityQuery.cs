namespace CSharpGameEngine.Entities;

/// <summary>
/// <c>GameSystemGenerator</c>が生成する、1つの<c>[Update]</c>メソッドに対応するQuery。
/// All(必須)/None(除外)/access(列解決対象、パラメータ順)の3つの型Id配列を保持する。
/// <see cref="World"/>のArchetypeリストは生成専用(append-only)なので、<see cref="Update"/>は
/// 前回スキャンした件数との差分だけを走査する - 新しいArchetypeが1つも生まれていないフレームは
/// int比較1回で完了する。列インデックスの2分探索は新Archetypeを発見した瞬間に1回だけ行う。
/// </summary>
public sealed class EntityQuery
{
    private readonly int[] access;
    private readonly ComponentMask allMask;
    private readonly ComponentMask noneMask;

    private Archetype[] matchedArchetypes = new Archetype[4];
    private int[] matchedColumns;
    private int matchedCount;
    private int scannedArchetypeCount;

    private EntityQuery(int[] all, int[] none, int[] access)
    {
        this.access = access;
        this.allMask = ComponentMask.FromSortedIds(all);
        this.noneMask = ComponentMask.FromSortedIds(none);
        this.matchedColumns = new int[this.matchedArchetypes.Length * access.Length];
    }

    /// <summary>
    /// <paramref name="all"/>/<paramref name="none"/>はパラメータ順で渡してよい
    /// (型IdはSGにとってコンパイル時未知のため、ソートは実行時にここで行う)。
    /// <paramref name="access"/>はパラメータ順のまま保持する(列インデックス解決の並びを決めるため)。
    /// </summary>
    public static EntityQuery Create(int[] all, int[] none, int[] access)
    {
        int[] sortedAll = (int[])all.Clone();
        Array.Sort(sortedAll);
        int[] sortedNone = (int[])none.Clone();
        Array.Sort(sortedNone);
        return new EntityQuery(sortedAll, sortedNone, access);
    }

    public QueryMatches Update(World world)
    {
        int total = world.ArchetypeCount;

        if (this.scannedArchetypeCount != total)
        {
            for (int i = this.scannedArchetypeCount; i < total; i++)
            {
                Archetype archetype = world.GetArchetype(i);

                if (!archetype.Mask.ContainsAll(this.allMask))
                {
                    continue;
                }

                if (archetype.Mask.OverlapsAny(this.noneMask))
                {
                    continue;
                }

                this.AppendMatch(archetype);
            }

            this.scannedArchetypeCount = total;
        }

        return new QueryMatches(this.matchedArchetypes, this.matchedColumns, this.access.Length, this.matchedCount);
    }

    private void AppendMatch(Archetype archetype)
    {
        if (this.matchedCount == this.matchedArchetypes.Length)
        {
            int newCapacity = this.matchedArchetypes.Length * 2;
            Array.Resize(ref this.matchedArchetypes, newCapacity);
            Array.Resize(ref this.matchedColumns, newCapacity * this.access.Length);
        }

        this.matchedArchetypes[this.matchedCount] = archetype;
        int baseOffset = this.matchedCount * this.access.Length;

        for (int k = 0; k < this.access.Length; k++)
        {
            this.matchedColumns[baseOffset + k] = archetype.IndexOfColumn(this.access[k]);
        }

        this.matchedCount++;
    }
}
