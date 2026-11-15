using System.Collections.Immutable;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Scene DSLの<c>new Node { … }</c>1つ。
///
/// <para>
/// <see cref="ComponentsListLocation"/>/<see cref="ChildrenListLocation"/>は、VSIXがPresetや
/// ドラッグ&amp;ドロップでC#ソースへ要素を挿入するときの挿入先を指す。該当メンバーが未記述なら
/// <c>null</c>で、その場合はオブジェクト初期化子(<see cref="InitializerLocation"/>)へ
/// <c>Components = [ … ]</c>ごと追加する必要がある。
/// </para>
/// </summary>
internal sealed class SceneNode(
        NodePath Path,
        string?Name,
        ImmutableArray<SceneComponent>Components,
        ImmutableArray<SceneNode>Children,
        SceneSourceLocation Location,
        SceneSourceLocation?InitializerLocation,
        SceneSourceLocation?NameValueLocation,
        SceneSourceLocation?ComponentsListLocation,
        SceneSourceLocation?ChildrenListLocation){

    /// <summary>このノードの唯一の同一性(<see cref="NodePath"/>参照)。</summary>
    internal NodePath Path { get; }=Path;

    /// <summary>コンパイル時定数の文字列。未指定なら<c>null</c>。<b>一意性は保証しない</b>。</summary>
    internal string?Name { get; }=Name;

    internal ImmutableArray<SceneComponent>Components { get; }=Components;

    internal ImmutableArray<SceneNode>Children { get; }=Children;

    /// <summary><c>new Node { … }</c>全体。</summary>
    internal SceneSourceLocation Location { get; }=Location;

    /// <summary><c>{ … }</c>のオブジェクト初期化子。初期化子が無ければ<c>null</c>。</summary>
    internal SceneSourceLocation?InitializerLocation { get; }=InitializerLocation;

    /// <summary><c>Name = "Floor"</c>の右辺。リネーム時の書き換え先。</summary>
    internal SceneSourceLocation?NameValueLocation { get; }=NameValueLocation;

    /// <summary><c>Components = [ … ]</c>の<c>[ … ]</c>。Component追加時の挿入先。</summary>
    internal SceneSourceLocation?ComponentsListLocation { get; }=ComponentsListLocation;

    /// <summary><c>Children = [ … ]</c>の<c>[ … ]</c>。子ノード追加時の挿入先。</summary>
    internal SceneSourceLocation?ChildrenListLocation { get; }=ChildrenListLocation;

    /// <summary>UI表示用。<c>Name</c>が無いノードもHierarchyでは何か表示する必要がある。</summary>
    internal string DisplayName=>this.Name is { Length:>0 } Value?Value:this.Path.ToDisplayString();

    internal SceneComponent?FindComponent(string TypeFullyQualifiedName){
        foreach(var Component in this.Components){
            if(string.Equals(Component.TypeFullyQualifiedName,TypeFullyQualifiedName,StringComparison.Ordinal)){
                return Component;
            }
        }

        return null;
    }
}
