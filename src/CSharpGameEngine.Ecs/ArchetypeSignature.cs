namespace CSharpGameEngine.Ecs;

/// <summary>
/// 正規化済みコンポーネント型配列の、要素シーケンス一致による同一性を表す。
/// 配列インスタンスそのものではなく中身(<see cref="Type"/>の並び)で比較するため、
/// 異なるアーキタイプ構造体であっても正規化後の構成が一致すれば同じシグネチャになり、
/// <see cref="World"/>内で同一の<see cref="ArchetypeStorage"/>を検索・共有できる。
/// </summary>
internal readonly struct ArchetypeSignature : IEquatable<ArchetypeSignature>
{
    private readonly Type[] componentTypes;

    internal ArchetypeSignature(Type[] componentTypes)
    {
        this.componentTypes = componentTypes;
    }

    public bool Equals(ArchetypeSignature other)
    {
        if (this.componentTypes.Length != other.componentTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < this.componentTypes.Length; i++)
        {
            if (this.componentTypes[i] != other.componentTypes[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is ArchetypeSignature other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hashCode = default;

        foreach (Type componentType in this.componentTypes)
        {
            hashCode.Add(componentType);
        }

        return hashCode.ToHashCode();
    }
}
