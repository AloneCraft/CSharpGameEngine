namespace CSharpGameEngine.VSExtension.Tests;

public sealed class HierarchyFocusPolicyTests {
    /// <summary>
    /// M0046: 本命は3行目「ガードOFF かつ フォーカス無し」。再解析で<c>ItemsSource</c>を差し替えた
    /// 後、WPFが遅延生成した<c>TreeViewItem</c>へスタイルSetter経由で<c>IsSelected</c>が入り、
    /// <c>Populate</c>の同期ガードが既に解けた状態で<c>SelectedItemChanged</c>が発火するケース。
    /// ここでtrueを返すと、コードエディタで入力中のユーザーからフォーカスを奪ってしまう。
    /// </summary>
    [Theory]
    [InlineData(false,true,true)]
    [InlineData(true,true,false)]
    [InlineData(false,false,false)]
    [InlineData(true,false,false)]
    public void ShouldPublishSelectionChange_RequiresTheTreeToAlreadyOwnKeyboardFocus(
        bool SuppressSelectionPublish,bool TreeHasKeyboardFocus,bool Expected){
        Assert.Equal(
            Expected,
            HierarchyFocusPolicy.ShouldPublishSelectionChange(SuppressSelectionPublish,TreeHasKeyboardFocus));
    }
}
