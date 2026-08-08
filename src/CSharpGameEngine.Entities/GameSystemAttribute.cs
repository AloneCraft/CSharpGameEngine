namespace CSharpGameEngine.Entities;

/// <summary>
/// このクラス内の<see cref="UpdateAttribute"/>が付いたメソッドを<c>GameSystemGenerator</c>が
/// 処理対象として認識する。クラス自身はEntity型を表すのではなく、単に処理メソッドの置き場所。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GameSystemAttribute : Attribute
{
}
