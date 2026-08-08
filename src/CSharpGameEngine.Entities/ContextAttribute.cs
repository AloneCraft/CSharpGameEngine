namespace CSharpGameEngine.Entities;

/// <summary>
/// このパラメータは全Entity共通の外部データであり、Query条件には無関係(ホイストされ、
/// 生成されたexecutorの引数としてそのまま転送される)。値型なら<c>in</c>、参照型なら値渡しで受ける。
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ContextAttribute : Attribute
{
}
