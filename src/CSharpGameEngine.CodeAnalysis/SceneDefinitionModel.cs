using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する(既存Model群と同じ理由)。
/// </remarks>

/// <summary>1つの<c>Set(new Foo { ... })</c>呼び出しが表す、1ノードが持つ1Componentの初期化。</summary>
internal readonly struct ComponentSetModel
{
    internal readonly string TypeFullyQualifiedName;
    internal readonly string InitializerExpressionText;

    internal ComponentSetModel(string typeFullyQualifiedName, string initializerExpressionText)
    {
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
        this.InitializerExpressionText = initializerExpressionText;
    }
}

/// <summary>
/// Define内の1つの<c>Create</c>呼び出しが表すScene Nodeの解析結果。<see cref="Children"/>を
/// 再帰的に持つことで、コード上の入れ子構造をそのまま木として表現する。<see cref="NodeId"/>は
/// このノードを表す<c>partial</c>プロパティの完全修飾シンボル名からAnalyzerが決定論的に導出する
/// (ユーザーが直書きすることはない、Rename後は再計算されるだけで値が変わること自体は無害という
/// Phase Aの設計判断は<see cref="global::CSharpGameEngine.Entities.SceneNodeId"/>のコメント参照)。
/// </summary>
internal readonly struct SceneNodeModel
{
    internal readonly string PropertyName;
    internal readonly ulong NodeId;
    internal readonly Location PropertyLocation;
    internal readonly ImmutableArray<ComponentSetModel> Components;
    internal readonly ImmutableArray<SceneNodeModel> Children;

    internal SceneNodeModel(
        string propertyName,
        ulong nodeId,
        Location propertyLocation,
        ImmutableArray<ComponentSetModel> components,
        ImmutableArray<SceneNodeModel> children)
    {
        this.PropertyName = propertyName;
        this.NodeId = nodeId;
        this.PropertyLocation = propertyLocation;
        this.Components = components;
        this.Children = children;
    }
}

/// <summary>
/// 1つの<c>[SceneDefinition]</c>クラスの解析結果。<see cref="Diagnostics"/>が1件でもあれば、
/// このクラス分の生成(SceneNodeSymbolプロパティ本体・CreateRuntime)を丸ごとスキップする
/// (GameSystemClassResultと同じ診断ゲートパターン)。<see cref="UsingDirectives"/>は、
/// <see cref="ComponentSetModel.InitializerExpressionText"/>をSGが生成コードへそのまま貼り付ける際、
/// <c>Vector3.One</c>のようなユーザーコード中の非完全修飾参照を解決できるようにするため、
/// 元のファイルのusingディレクティブをそのままの文字列で保持しておく(意味解析による完全修飾名への
/// 書き換えは行わない — Phase Aでは元ファイル冒頭のusingをそのまま生成ファイルへ複製する方針で十分)。
/// </summary>
internal readonly struct SceneDefinitionClassResult
{
    internal readonly string? Namespace;
    internal readonly string ClassName;
    internal readonly ImmutableArray<string> UsingDirectives;
    internal readonly SceneNodeModel? Root;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    internal SceneDefinitionClassResult(
        string? @namespace,
        string className,
        ImmutableArray<string> usingDirectives,
        SceneNodeModel? root,
        ImmutableArray<Diagnostic> diagnostics)
    {
        this.Namespace = @namespace;
        this.ClassName = className;
        this.UsingDirectives = usingDirectives;
        this.Root = root;
        this.Diagnostics = diagnostics;
    }

    internal string ClassFullyQualifiedName => this.Namespace is null ? this.ClassName : $"{this.Namespace}.{this.ClassName}";
}
