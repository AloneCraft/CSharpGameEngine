namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Hierarchyのノード並べ替えドラッグにおける挿入位置判定。<c>Tree_DragOver</c>の挿入位置プレビューと
/// <c>Tree_Drop</c>の実際の適用が同じ結果になることを固定する(境界値を含む)。
/// </summary>
public sealed class HierarchyInsertionPolicyTests {
    [Fact]
    public void Resolve_TopQuarter_IsBefore()=>
        Assert.Equal(HierarchyInsertionKind.Before,HierarchyInsertionPolicy.Resolve(RelativeY:0d,Height:100d));

    [Fact]
    public void Resolve_JustBelowTopQuarterBoundary_IsBefore()=>
        Assert.Equal(HierarchyInsertionKind.Before,HierarchyInsertionPolicy.Resolve(RelativeY:24.9d,Height:100d));

    [Fact]
    public void Resolve_AtTopQuarterBoundary_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:25d,Height:100d));

    [Fact]
    public void Resolve_Middle_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:50d,Height:100d));

    [Fact]
    public void Resolve_AtBottomQuarterBoundary_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:75d,Height:100d));

    [Fact]
    public void Resolve_JustAboveBottomQuarterBoundary_IsAfter()=>
        Assert.Equal(HierarchyInsertionKind.After,HierarchyInsertionPolicy.Resolve(RelativeY:75.1d,Height:100d));

    [Fact]
    public void Resolve_BottomQuarter_IsAfter()=>
        Assert.Equal(HierarchyInsertionKind.After,HierarchyInsertionPolicy.Resolve(RelativeY:100d,Height:100d));

    /// <summary>コンテナがまだレイアウトされておらず高さが取れない異常値は、安全側(子として取り込む)へ倒す。</summary>
    [Fact]
    public void Resolve_ZeroHeight_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:0d,Height:0d));

    [Fact]
    public void Resolve_NegativeHeight_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:0d,Height:-1d));
}
