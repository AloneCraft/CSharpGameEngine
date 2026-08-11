namespace CSharpGameEngine.Entities;

/// <summary>
/// <c>[StartupScene]</c>クラス内で、1つの論理Entityが持つComponent初期値を宣言する<c>static</c>
/// プロパティに付ける。戻り値が単一の非nullable structなら1 Componentのみを持つEntity、
/// <c>ValueTuple</c>ならその各要素(すべて非nullable struct、要素名必須)を持つEntityになる。
///
/// <para>
/// ランタイムに存在する<c>Entity</c>型との名前上の衝突・混同を避けるため<c>[SceneEntity]</c>という
/// 名前にしている(このプロパティ自体はEntityそのものではなく、Entity生成に使うComponent値の宣言)。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SceneEntityAttribute : Attribute
{
}
