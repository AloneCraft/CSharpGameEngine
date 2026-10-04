namespace CSharpGameEngine.Entities;

/// <summary>
/// Removed by ADR-0073. The execution order is declared relative to other systems with
/// <see cref="RunsAfterAttribute"/> and <see cref="RunsBeforeAttribute"/>; systems with no declared order run by
/// full name. The type remains so that existing code gets an error that names the replacement.
/// </summary>
[Obsolete("ADR-0073: [SystemOrder] was removed. Declare the order relative to other systems with [RunsAfter(typeof(...))] or [RunsBefore(typeof(...))]; systems with no declared order run by full name.",error:true)]
[AttributeUsage(AttributeTargets.Struct|AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class SystemOrderAttribute(int Order):Attribute {
    public int Order { get; }=Order;
}
