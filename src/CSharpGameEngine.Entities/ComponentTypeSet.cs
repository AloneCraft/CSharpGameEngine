namespace CSharpGameEngine.Entities;

/// <summary>
/// 正規化済み(昇順ソート済み)コンポーネント型Id配列の、要素シーケンス一致による同一性。
/// 旧CSharpGameEngine.Ecsの<c>ArchetypeSignature</c>(<c>Type[]</c>の参照比較を伴うシーケンス一致)を
/// int比較に置き換えたもの。Archetype辞書のキーとしてのみ使う(Archetype生成時、
/// すなわちコールドパスでのみ比較される)。
/// </summary>
internal readonly struct ComponentTypeSet : IEquatable<ComponentTypeSet>
{
    private readonly int[] sortedComponentTypeIds;

    internal ComponentTypeSet(int[] sortedComponentTypeIds)
    {
        this.sortedComponentTypeIds = sortedComponentTypeIds;
    }

    public bool Equals(ComponentTypeSet other)
    {
        if (this.sortedComponentTypeIds.Length != other.sortedComponentTypeIds.Length)
        {
            return false;
        }

        for (int i = 0; i < this.sortedComponentTypeIds.Length; i++)
        {
            if (this.sortedComponentTypeIds[i] != other.sortedComponentTypeIds[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is ComponentTypeSet other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hashCode = default;

        foreach (int id in this.sortedComponentTypeIds)
        {
            hashCode.Add(id);
        }

        return hashCode.ToHashCode();
    }
}
