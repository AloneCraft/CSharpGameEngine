namespace CSharpGameEngine.VSExtension;

/// <summary>Hierarchyのノード並べ替えドラッグにおける挿入位置。</summary>
internal enum HierarchyInsertionKind {
    /// <summary>対象ノードの直前(同じ親の兄弟として)。</summary>
    Before,

    /// <summary>対象ノードの直後(同じ親の兄弟として)。</summary>
    After,

    /// <summary>対象ノードの子として取り込む。</summary>
    Into,
}

/// <summary>
/// M0052(拡張): ドラッグ中のプレビュー表示(<c>Tree_DragOver</c>)と実際の適用
/// (<c>Tree_Drop</c>)が同じ計算式を共有するための純粋関数。<see cref="ComponentDropPolicy"/>と
/// 同じ思想 — プレビューと結果が食い違うのが一番わかりにくいため、両方をここに集約する。
/// </summary>
internal static class HierarchyInsertionPolicy {
    /// <summary>
    /// <paramref name="RelativeY"/>は対象アイテム上端からの相対Y座標、<paramref name="Height"/>は
    /// そのアイテムの高さ。上25%未満で<see cref="HierarchyInsertionKind.Before"/>、下25%超で
    /// <see cref="HierarchyInsertionKind.After"/>、中央50%(および高さが0以下の異常値)では
    /// <see cref="HierarchyInsertionKind.Into"/>を返す。
    /// </summary>
    internal static HierarchyInsertionKind Resolve(float RelativeY,float Height){
        if(Height<=0f){
            return HierarchyInsertionKind.Into;
        }

        if(RelativeY<Height*.25f){
            return HierarchyInsertionKind.Before;
        }

        return RelativeY>Height*.75f?HierarchyInsertionKind.After:HierarchyInsertionKind.Into;
    }
}
