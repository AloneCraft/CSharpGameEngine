namespace CSharpGameEngine.Entities;

/// <summary>
/// このメソッドを1つの独立した実行単位として<c>GameSystemGenerator</c>に処理させる。Query条件
/// (All/None)とRead/Writeアクセスはパラメータの書き方から推論される(<c>ref</c>=Write、
/// <c>in</c>=Read、<see cref="ContextAttribute"/>/<see cref="CurrentEntityAttribute"/>/
/// <see cref="CommandsAttribute"/>はQueryに無関係)。<see cref="Order"/>は同じComponentへの
/// Read/Writeから決定できない場合の実行順序のタイブレークにのみ使う。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UpdateAttribute : Attribute
{
    public int Order { get; set; }
}
