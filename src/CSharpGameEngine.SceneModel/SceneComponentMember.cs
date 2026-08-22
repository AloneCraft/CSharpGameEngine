using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// オブジェクト初期化子の代入1つ(<c>Position = new(0, 0, 0)</c>)。
///
/// <para>
/// <see cref="Location"/>は代入式<b>全体</b>、<see cref="SceneValue.Location"/>は<b>右辺だけ</b>を指す。
/// Inspectorが値を差し替えるときは後者(最小差分)、メンバーごと削除するときは前者を使う。
/// </para>
/// </summary>
internal sealed class SceneComponentMember(string MemberName,ISymbol?MemberSymbol,SceneValue Value,SceneSourceLocation Location) {
    internal string MemberName { get; }=MemberName;

    /// <summary><c>IPropertySymbol</c>または<c>IFieldSymbol</c>。未解決なら<c>null</c>。</summary>
    internal ISymbol?MemberSymbol { get; }=MemberSymbol;

    internal SceneValue Value { get; }=Value;

    internal SceneSourceLocation Location { get; }=Location;
}
