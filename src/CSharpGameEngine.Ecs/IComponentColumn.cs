namespace CSharpGameEngine.Ecs;

/// <summary>
/// アーキタイプストレージ内の1コンポーネント型に対応するSoA列。実装は<see cref="ComponentColumn{T}"/>のみ。
/// </summary>
public interface IComponentColumn
{
    void EnsureCapacity(int capacity);

    void MoveRow(int sourceRow, int destinationRow);
}
