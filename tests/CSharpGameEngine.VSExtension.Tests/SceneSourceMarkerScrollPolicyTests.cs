namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0043: ソースマーカーがコードエディタのスクロール位置へ触れてよいかの判定。
/// 中心的な契約は「再解析経路は絶対にスクロールしない」——ギズモ編集のたびに無関係な位置へ
/// 飛んでいた実機不具合(M0040/M0041/M0042でも解消しきれなかった)を、原因の特定ではなく
/// 構造として封じるための固定。
/// </summary>
public sealed class SceneSourceMarkerScrollPolicyTests {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void ShouldScroll_PreserveScroll_IsAlwaysFalse(int MarkerRangeCount){
        // マーク範囲があってもなくても、再解析経路からはスクロールが起きてはならない。
        Assert.False(SceneSourceMarkerScrollPolicy.ShouldScroll(
            SceneSourceMarkerScrollBehavior.PreserveScroll,MarkerRangeCount));
    }

    [Fact]
    public void ShouldScroll_ScrollToSelection_RequiresAtLeastOneRange(){
        // マークする範囲が無いのにスクロールすると、Ranges[0]が存在せず参照できない。
        Assert.False(SceneSourceMarkerScrollPolicy.ShouldScroll(
            SceneSourceMarkerScrollBehavior.ScrollToSelection,MarkerRangeCount:0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ShouldScroll_ScrollToSelection_WithRanges_IsTrue(int MarkerRangeCount){
        Assert.True(SceneSourceMarkerScrollPolicy.ShouldScroll(
            SceneSourceMarkerScrollBehavior.ScrollToSelection,MarkerRangeCount));
    }
}
