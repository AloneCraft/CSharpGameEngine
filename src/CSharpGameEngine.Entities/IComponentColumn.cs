namespace CSharpGameEngine.Entities;

/// <summary>
/// アーキタイプ内の1コンポーネント型に対応するSoA列。実装は<see cref="ComponentColumn{T}"/>のみ。
/// <see cref="CopyRowFrom"/>/<see cref="SetDefault"/>は構造変更(Archetype間のEntity移動)専用で、
/// Query一致後の行ループ(ホットパス)からは呼ばれない。
/// </summary>
public interface IComponentColumn
{
    void EnsureCapacity(int capacity);

    void MoveRow(int sourceRow, int destinationRow);

    /// <summary>構造変更でEntityが移動してきたとき、移動元の同じコンポーネント型の列から1行分だけ値をコピーする。</summary>
    void CopyRowFrom(IComponentColumn source, int sourceRow, int destinationRow);

    /// <summary>構造変更でEntityが移動してきたとき、移動先にのみ存在する(新規追加された)列の値を既定値にする。</summary>
    void SetDefault(int row);
}
