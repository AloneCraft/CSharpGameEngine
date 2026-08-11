namespace CSharpGameEngine.Entities;

/// <summary>
/// このクラス内の<c>static partial SceneNodeSymbol</c>プロパティとpublicな<c>Define</c>メソッドを
/// <c>SceneDefinitionGenerator</c>が処理対象として認識する。クラス自身はSceneの正本であり、
/// Hierarchy Docking Window(将来のPhase B)もこのクラスの構文木を直接解析・編集する対象になる。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SceneDefinitionAttribute:Attribute {
}
