namespace CSharpGameEngine.Entities;

/// <summary>
/// Id + Generationで表現するエンティティハンドル。Generationは破棄のたびに増分され、
/// 既に破棄・再利用されたIdに対する古いハンドル(stale handle)を検出できる。
/// Idが負の場合は、<see cref="EntityCommandBuffer"/>内でのみ有効な一時ハンドル
/// (まだWorldに存在しないEntityへの前方参照)を表す。<see cref="World.IsAlive"/>の判定は
/// Id&gt;=0を前提とするため、一時ハンドルは常に「生存していない」と判定される。
/// </summary>
public readonly struct Entity : IEquatable<Entity>
{
    public readonly int Id;
    public readonly int Generation;

    public Entity(int id, int generation)
    {
        this.Id = id;
        this.Generation = generation;
    }

    internal static Entity Temporary(int pendingIndex)
    {
        return new Entity(-(pendingIndex + 1), 0);
    }

    internal bool IsTemporary => this.Id < 0;

    internal int TemporaryIndex => -(this.Id + 1);

    public bool Equals(Entity other)
    {
        return this.Id == other.Id && this.Generation == other.Generation;
    }

    public override bool Equals(object? obj)
    {
        return obj is Entity other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Id, this.Generation);
    }

    public override string ToString()
    {
        return $"Entity({this.Id}:{this.Generation})";
    }

    public static bool operator ==(Entity left, Entity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Entity left, Entity right)
    {
        return !left.Equals(right);
    }
}
