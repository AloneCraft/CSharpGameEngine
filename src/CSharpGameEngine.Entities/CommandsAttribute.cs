namespace CSharpGameEngine.Entities;

/// <summary>このパラメータには<see cref="World.Commands"/>(構造変更用の<see cref="EntityCommandBuffer"/>)が渡される。値渡しの<see cref="EntityCommandBuffer"/>でなければならない。</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CommandsAttribute : Attribute
{
}
