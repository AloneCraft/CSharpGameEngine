using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// ノードの<c>Components</c>配列の要素1つ。<b>Runtime Componentそのもの</b>であり、
/// <c>TransformDeclaration</c>のような二重型は存在しない(ADR-0040)。
///
/// <para>
/// <c>Node.Components</c>が<c>object[]</c>なのは異なるComponent型を1つのコレクション式へ入れるための
/// 構文上の容器にすぎず、Scene DSLは実行されないのでboxingはRuntimeコストにならない。ただし
/// <b>型システムが<c>null</c>・文字列・<c>Node</c>の混入を止められない</b>ため、それらは
/// <see cref="SceneDeclarationAnalyzer"/>がSCENE107として明示的に弾く。
/// </para>
/// </summary>
internal sealed class SceneComponent(
        INamedTypeSymbol?TypeSymbol,
        string TypeFullyQualifiedName,
        ImmutableArray<SceneValue>ConstructorArguments,
        ImmutableArray<SceneComponentMember>Members,
        string EmitExpressionText,
        SceneSourceLocation Location,
        SceneComponentOrderKey?OrderKey=null){

    /// <summary>型が未解決(入力途中)なら<c>null</c>。</summary>
    internal INamedTypeSymbol?TypeSymbol { get; }=TypeSymbol;

    /// <summary><c>global::</c>接頭辞付き。Archetype登録のキーになる。</summary>
    internal string TypeFullyQualifiedName { get; }=TypeFullyQualifiedName;

    internal ImmutableArray<SceneValue>ConstructorArguments { get; }=ConstructorArguments;

    internal ImmutableArray<SceneComponentMember>Members { get; }=Members;

    /// <summary>生成コードへそのまま出せる完全修飾済みの<c>new T { … }</c>式。</summary>
    internal string EmitExpressionText { get; }=EmitExpressionText;

    /// <summary><c>new T { … }</c>全体。</summary>
    internal SceneSourceLocation Location { get; }=Location;

    /// <summary>正規化に使うカテゴリ、明示順、完全修飾型名の比較キー。</summary>
    internal SceneComponentOrderKey OrderKey { get; }=OrderKey??SceneComponentOrdering.GetSortKey(TypeSymbol,TypeFullyQualifiedName);

    internal SceneComponentMember?FindMember(string MemberName){
        foreach(var Member in this.Members){
            if(string.Equals(Member.MemberName,MemberName,StringComparison.Ordinal)){
                return Member;
            }
        }

        return null;
    }
}
