using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0052: パレットに1行として並ぶComponent型。
///
/// <para>
/// <see cref="SceneComponentCatalogEntry"/>と違い<b><c>INamedTypeSymbol</c>を保持しない</b>のが要点。
/// パレットの一覧はProject単位でキャッシュしてUIが持ち続けるため、シンボルを持たせると
/// その<c>Compilation</c>全体をピン留めしてしまう(Roslynのシンボルは生成元Compilationを参照する)。
/// 追加に必要な情報は完全修飾名と生成式の文字列だけなので、その形へ射影してから持つ。
/// </para>
/// </summary>
internal sealed class ComponentPaletteEntry {
    internal ComponentPaletteEntry(
        string FullyQualifiedName,string DisplayName,string Namespace,string ComponentExpression,SceneVisualIcon Icon){
        this.FullyQualifiedName=FullyQualifiedName;
        this.DisplayName=DisplayName;
        this.Namespace=Namespace;
        this.ComponentExpression=ComponentExpression;
        this.Icon=Icon;
    }

    internal string FullyQualifiedName { get; }

    // ComponentPaletteToolWindowControl.xaml から {Binding DisplayName}/{Binding Namespace}/
    // {Binding Icon}でバインドされるため、この3つは必ずpublicにする。WPFのバインディングは
    // publicプロパティしか解決できず、internalだと例外も警告もなく黙って失敗する
    // (Inspectorが空になった過去の不具合と同じ罠。InternalsVisibleToが効くテストでは
    // internalでも読めてしまうため、通常の単体テストでは気づけない)。

    /// <summary>"StatusEffect&lt;PoisonKind&gt;"のような、名前空間を落とした読める名前。</summary>
    public string DisplayName { get; }

    public string Namespace { get; }

    public SceneVisualIcon Icon { get; }

    /// <summary>
    /// <see cref="AddComponentOperation.ComponentExpression"/>へ渡すC#式。書き込み可能メンバーを
    /// 明示的に列挙した<c>new T { A=default, … }</c>で、コンストラクタが設定する既定値は保たれる
    /// (<see cref="SceneComponentInitializerPolicy"/>)。シンボルが生きているカタログ構築時に
    /// 組み立て済みのものを受け取るだけなので、この型は<c>INamedTypeSymbol</c>を持たずに済む。
    /// </summary>
    internal string ComponentExpression { get; }
}

/// <summary>
/// M0052: パレットの絞り込み。generic型は1つの定義から最大50件のinstantiationへ展開されるため、
/// 検索が無いと一覧が実用にならない。
/// </summary>
internal static class ComponentPaletteFilterPolicy {
    internal static IReadOnlyList<ComponentPaletteEntry>Filter(
        IReadOnlyList<ComponentPaletteEntry>Entries,string?Query)=>
        IsEmptyQuery(Query)
            ?Entries
            :[..Entries.Where(Entry=>Matches(Entry.DisplayName,Entry.Namespace,Query))];

    /// <summary>
    /// 空白区切りの語は<b>AND</b>で、語ごとに表示名と名前空間のどちらかに含まれていればよい。
    /// 大小は区別しない。<c>&lt;</c>や<c>&gt;</c>も普通の文字として扱うので
    /// "effect&lt;poison" のような閉じたgenericの探し方がそのまま効く。
    /// </summary>
    internal static bool Matches(string DisplayName,string Namespace,string?Query){
        if(IsEmptyQuery(Query)){
            return true;
        }

        foreach(var Term in Query!.Split([' ','\t'],StringSplitOptions.RemoveEmptyEntries)){
            if(DisplayName.IndexOf(Term,StringComparison.OrdinalIgnoreCase)<0
                &&Namespace.IndexOf(Term,StringComparison.OrdinalIgnoreCase)<0){
                return false;
            }
        }

        return true;
    }

    private static bool IsEmptyQuery(string?Query)=>string.IsNullOrWhiteSpace(Query);
}
