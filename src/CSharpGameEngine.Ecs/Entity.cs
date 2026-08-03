namespace CSharpGameEngine.Ecs;

/// <summary>
/// Id + Generationで表現するエンティティハンドル。Generationは破棄のたびに増分され、
/// 既に破棄・再利用されたIdに対する古いハンドル(stale handle)を検出できる。
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
