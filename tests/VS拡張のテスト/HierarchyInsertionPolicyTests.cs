namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Hierarchyのノード並べ替えドラッグにおける挿入位置判定。<c>Tree_DragOver</c>の挿入位置プレビューと
/// <c>Tree_Drop</c>の実際の適用が同じ結果になることを固定する(境界値を含む)。
/// </summary>
public sealed class HierarchyInsertionPolicyTests {
    [Fact]
    public void Resolve_TopQuarter_IsBefore()=>
        Assert.Equal(HierarchyInsertionKind.Before,HierarchyInsertionPolicy.Resolve(RelativeY:0f,Height:100f));

    [Fact]
    public void Resolve_JustBelowTopQuarterBoundary_IsBefore()=>
        Assert.Equal(HierarchyInsertionKind.Before,HierarchyInsertionPolicy.Resolve(RelativeY:24.9f,Height:100f));

    [Fact]
    public void Resolve_AtTopQuarterBoundary_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:25f,Height:100f));

    [Fact]
    public void Resolve_Middle_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:50f,Height:100f));

    [Fact]
    public void Resolve_AtBottomQuarterBoundary_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:75f,Height:100f));

    [Fact]
    public void Resolve_JustAboveBottomQuarterBoundary_IsAfter()=>
        Assert.Equal(HierarchyInsertionKind.After,HierarchyInsertionPolicy.Resolve(RelativeY:75.1f,Height:100f));

    [Fact]
    public void Resolve_BottomQuarter_IsAfter()=>
        Assert.Equal(HierarchyInsertionKind.After,HierarchyInsertionPolicy.Resolve(RelativeY:100f,Height:100f));

    /// <summary>コンテナがまだレイアウトされておらず高さが取れない異常値は、安全側(子として取り込む)へ倒す。</summary>
    [Fact]
    public void Resolve_ZeroHeight_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:0f,Height:0f));

    [Fact]
    public void Resolve_NegativeHeight_IsInto()=>
        Assert.Equal(HierarchyInsertionKind.Into,HierarchyInsertionPolicy.Resolve(RelativeY:0f,Height:-1f));
}
