namespace CSharpGameEngine.Entities;

/// <summary>
/// Scene定義コード上の1ノードを指す決定論的なID。<c>SceneDefinitionGenerator</c>が、そのノードを
/// 表す<c>partial</c>プロパティの完全修飾シンボル名から生成する。ユーザーが生の数値やGUIDを
/// 直接書くことはない。このIDはRuntime側では一切永続化・参照されず、ビルドのたびに再計算される
/// だけなので、Rename後に値が変わること自体は無害(Phase A時点の設計判断)。将来、IDをまたいで
/// 安定させる必要のある消費者(セーブデータでのノード参照など)が生まれた時点で、IDE管理の
/// hidden annotationのような別の仕組みを別途検討する。
/// </summary>
public readonly record struct SceneNodeId(ulong Value);
