namespace CSharpGameEngine.Entities;

/// <summary>
/// ユーザーコード上でScene Nodeを識別するシンボル。実体の<see cref="Entity"/>そのものではなく、
/// <c>SceneDefinitionGenerator</c>が生成する<c>static partial SceneNodeSymbol</c>プロパティの
/// 戻り値として現れる、コンパイル時に確定した「このノードを指す」というマーカー。
/// </summary>
public readonly record struct SceneNodeSymbol(SceneNodeId Id, string Name);
