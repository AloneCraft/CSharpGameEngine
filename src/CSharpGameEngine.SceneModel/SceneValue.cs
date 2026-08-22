using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Scene DSLで許可される値式の種別。<b>whitelist(default-deny)</b>であり、ここに無い構文はすべて
/// <see cref="Unsupported"/>になる(ADR-0040)。禁止構文を列挙する方式では<c>with</c>式・パターン・
/// 非定数の補間文字列などが漏れるため、許可する形だけを数え上げる。
/// </summary>
internal enum SceneValueKind {
    /// <summary><c>42</c> / <c>"x"</c> / <c>true</c> / <c>-1.5f</c>。</summary>
    Literal,

    /// <summary><c>12f*MathF.PI/180f</c> / <c>nameof(X)</c> / const参照 — <c>GetConstantValue</c>が成功する式。</summary>
    ConstantExpression,

    /// <summary><c>PrimitiveType.Cube</c>。</summary>
    EnumMember,

    /// <summary><c>default</c> / <c>default(T)</c>。</summary>
    Default,

    /// <summary><c>new Vector3(0,0,0)</c> / <c>new Transform { Position = … }</c> / <c>new(…)</c>。</summary>
    ObjectCreation,

    /// <summary><c>[ … ]</c> / <c>new[] { … }</c> / <c>new T[] { … }</c>。</summary>
    Collection,

    /// <summary><c>Assets.Floor</c> — <c>[SceneAssets]</c>を付けた静的クラスのメンバー参照のみ。</summary>
    StaticMemberRef,

    /// <summary><c>Camera.CreateFirstPerson(…)</c> — <c>[SceneFactory]</c>を付けた静的メソッドのみ。</summary>
    FactoryCall,

    /// <summary>
    /// 文法違反、または型が未解決(ユーザーが入力中)。前者は診断が出るが、後者は<b>診断を出さない</b>
    /// (別のコンパイルエラーが既に出ているため)。
    /// </summary>
    Unsupported,
}

/// <summary>
/// Scene DSLの値式1つ。解析層の型であり、<c>ITypeSymbol</c>/<c>ISymbol</c>を保持する。
/// <b>この型を<c>IncrementalValuesProvider</c>へ流してはならない</b>(シンボルが<c>Compilation</c>を
/// ルート保持してキャッシュを無効化する) — SG側は<c>SceneEmitModel</c>へ変換してから流す。
///
/// <para>
/// 再帰的な構造のため<c>readonly struct</c>ではなく<c>sealed class</c>にしている。解析層はホットパスでは
/// ないので割り当ては問題にならず、structのレイアウト循環リスクを完全に避けられる。
/// </para>
/// </summary>
internal sealed class SceneValue(
        SceneValueKind Kind,
        ITypeSymbol?Type,
        object?ConstantValue,
        ISymbol?ReferencedSymbol,
        ImmutableArray<SceneComponentMember>Members,
        ImmutableArray<SceneValue>Arguments,
        ImmutableArray<SceneValue>Elements,
        string EmitExpressionText,
        string SourceExpressionText,
        SceneSourceLocation Location) {

    internal SceneValueKind Kind { get; }=Kind;

    internal ITypeSymbol?Type { get; }=Type;

    /// <summary><see cref="SceneValueKind.Literal"/>/<see cref="SceneValueKind.ConstantExpression"/>/<see cref="SceneValueKind.EnumMember"/>のとき設定される。</summary>
    internal object?ConstantValue { get; }=ConstantValue;

    /// <summary><see cref="SceneValueKind.EnumMember"/>/<see cref="SceneValueKind.StaticMemberRef"/>/<see cref="SceneValueKind.FactoryCall"/>のとき設定される。</summary>
    internal ISymbol?ReferencedSymbol { get; }=ReferencedSymbol;

    /// <summary><see cref="SceneValueKind.ObjectCreation"/>のオブジェクト初期化子。</summary>
    internal ImmutableArray<SceneComponentMember>Members { get; }=Members;

    /// <summary><see cref="SceneValueKind.ObjectCreation"/>の位置引数、または<see cref="SceneValueKind.FactoryCall"/>の引数。</summary>
    internal ImmutableArray<SceneValue>Arguments { get; }=Arguments;

    /// <summary><see cref="SceneValueKind.Collection"/>の要素。</summary>
    internal ImmutableArray<SceneValue>Elements { get; }=Elements;

    /// <summary>
    /// 生成コードへそのまま出せる<b>完全修飾済み</b>の式テキスト。usingディレクティブに依存しない
    /// (旧SceneRuntimeEmitterはソースを逐語コピーしてusingも一緒に運んでいたが、using aliasと
    /// 同名型で破綻するため廃止した)。
    /// </summary>
    internal string EmitExpressionText { get; }=EmitExpressionText;

    /// <summary>
    /// 元のソーステキスト。<b>Inspectorの表示にはこちらを使う</b> — <see cref="SceneValueKind.ConstantExpression"/>は
    /// <see cref="EmitExpressionText"/>で値へ畳み込まれてしまうため、ユーザーが書いた
    /// <c>12f*MathF.PI/180f</c>をそのまま見せるにはこれが要る。
    /// </summary>
    internal string SourceExpressionText { get; }=SourceExpressionText;

    internal SceneSourceLocation Location { get; }=Location;

    /// <summary>
    /// Inspectorが数値入力やenumピッカーで<b>直接編集できる</b>か。定数式・静的参照・ファクトリ呼び出しは
    /// 元の式の意図を壊さずに値だけ差し替えることができないため、読み取り専用として扱う。
    /// </summary>
    internal bool IsEditable=>this.Kind is SceneValueKind.Literal or SceneValueKind.EnumMember or SceneValueKind.Default;

    internal static SceneValue Unsupported(string SourceExpressionText,SceneSourceLocation Location){
        return new SceneValue(
            SceneValueKind.Unsupported,
            Type:null,
            ConstantValue:null,
            ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,
            ImmutableArray<SceneValue>.Empty,
            ImmutableArray<SceneValue>.Empty,
            EmitExpressionText:"default",
            SourceExpressionText,
            Location);
    }
}
