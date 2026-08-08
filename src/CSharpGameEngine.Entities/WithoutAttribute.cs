namespace CSharpGameEngine.Entities;

/// <summary>
/// このメソッドのQueryのNone(除外)集合に<see cref="ComponentType"/>を加える。「引数から推論する」
/// という原則の唯一の例外 - 除外条件は引数の書き方では表現できないため用意する。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class WithoutAttribute : Attribute
{
    public WithoutAttribute(Type componentType)
    {
        this.ComponentType = componentType;
    }

    public Type ComponentType { get; }
}
