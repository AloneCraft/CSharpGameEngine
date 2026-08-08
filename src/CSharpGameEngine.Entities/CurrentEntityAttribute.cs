namespace CSharpGameEngine.Entities;

/// <summary>このパラメータには、現在処理中の行が属する<see cref="Entity"/>が渡される。値渡しの<see cref="Entity"/>でなければならない。</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CurrentEntityAttribute : Attribute
{
}
