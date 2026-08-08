namespace CSharpGameEngine.Entities;

/// <summary>
/// 固定256ビットのコンポーネント型集合。可変長<c>ulong[]</c>にすると、後から登録された型により
/// word数が伸びたときArchetypeとQueryのmask長がズレて比較がバグる。固定長ならその可能性自体が無い
/// (超過時は<see cref="ComponentTypeRegistry"/>が明確な例外を投げる)。<see cref="ContainsAll"/>/
/// <see cref="OverlapsAny"/>はいずれもO(4)・アロケーションなしで、Query側の差分スキャンでのみ呼ばれる。
/// </summary>
public readonly struct ComponentMask : IEquatable<ComponentMask>
{
    internal const int BitCount = 256;

    private readonly ulong word0;
    private readonly ulong word1;
    private readonly ulong word2;
    private readonly ulong word3;

    private ComponentMask(ulong word0, ulong word1, ulong word2, ulong word3)
    {
        this.word0 = word0;
        this.word1 = word1;
        this.word2 = word2;
        this.word3 = word3;
    }

    public static ComponentMask FromSortedIds(ReadOnlySpan<int> sortedComponentTypeIds)
    {
        ulong w0 = 0;
        ulong w1 = 0;
        ulong w2 = 0;
        ulong w3 = 0;

        foreach (int id in sortedComponentTypeIds)
        {
            ulong bit = 1UL << (id & 63);
            switch (id >> 6)
            {
                case 0:
                    w0 |= bit;
                    break;
                case 1:
                    w1 |= bit;
                    break;
                case 2:
                    w2 |= bit;
                    break;
                default:
                    w3 |= bit;
                    break;
            }
        }

        return new ComponentMask(w0, w1, w2, w3);
    }

    public bool ContainsAll(in ComponentMask required)
    {
        return (this.word0 & required.word0) == required.word0
            && (this.word1 & required.word1) == required.word1
            && (this.word2 & required.word2) == required.word2
            && (this.word3 & required.word3) == required.word3;
    }

    public bool OverlapsAny(in ComponentMask other)
    {
        return ((this.word0 & other.word0) | (this.word1 & other.word1)
            | (this.word2 & other.word2) | (this.word3 & other.word3)) != 0UL;
    }

    public bool Equals(ComponentMask other)
    {
        return this.word0 == other.word0 && this.word1 == other.word1
            && this.word2 == other.word2 && this.word3 == other.word3;
    }

    public override bool Equals(object? obj)
    {
        return obj is ComponentMask other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.word0, this.word1, this.word2, this.word3);
    }
}
