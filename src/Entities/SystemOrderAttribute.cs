namespace CSharpGameEngine.Entities;

/// <summary>
/// ADR-0073で削除。実行順は<see cref="RunsAfterAttribute"/>および<see cref="RunsBeforeAttribute"/>で他のシステムとの相対順として宣言する。
/// 順序指定のないシステムは完全名順で実行する。置き換え先を示すエラーを既存コードに表示するため、型自体は残す。
/// </summary>
[Obsolete("ADR-0073: [SystemOrder] was removed. Declare the order relative to other systems with [RunsAfter(typeof(...))] or [RunsBefore(typeof(...))]; systems with no declared order run by full name.",error:true)]
[AttributeUsage(AttributeTargets.Struct|AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class SystemOrderAttribute(int Order):Attribute {
    public int Order { get; }=Order;
}
