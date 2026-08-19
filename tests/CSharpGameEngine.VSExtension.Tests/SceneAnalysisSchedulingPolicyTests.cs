using System;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-6 Stage A: 再解析のスケジューリングを純粋関数として固定する。
///
/// <para>
/// 実機報告「保存すると(Ctrl+Z/Yが)遅くなる」の調査で、遅いだけでなく
/// <b>pin中Sceneの<c>.cs</c>がアクティブなコードエディタ文書でないと再解析が一切来ない</b>
/// (=表示が永久に追いつかない)ことと、<b>デバウンスが際限なく延び続ける</b>ことが判明した。
/// </para>
/// </summary>
public sealed class SceneAnalysisSchedulingPolicyTests {
    // ---- 解析対象 ----

    /// <summary>
    /// 不具合の中心。Scene Designerで作業している間、アクティブなコードエディタ文書は
    /// 別ファイルであることが普通にある。そのとき再解析対象に含めないと、Scene Designer自身が
    /// 書き換えたテキストにすら表示が追いつかない。
    /// </summary>
    [Fact]
    public void PinnedScene_IsAnalyzedWhenItIsNotTheActiveDocument(){
        Assert.True(SceneAnalysisTargetPolicy.ShouldAlsoAnalyzePinnedScene(
            ActiveDocumentPath:@"C:\Game\Other.cs",PinnedSceneFilePath:@"C:\Game\MainScene.cs"));
    }

    /// <summary>同じファイルなら二重解析しない(Compilation再バインドが支配的なので無駄が大きい)。</summary>
    [Fact]
    public void PinnedScene_IsNotAnalyzedTwiceWhenItIsTheActiveDocument(){
        Assert.False(SceneAnalysisTargetPolicy.ShouldAlsoAnalyzePinnedScene(
            ActiveDocumentPath:@"C:\Game\MainScene.cs",PinnedSceneFilePath:@"C:\Game\MainScene.cs"));
    }

    /// <summary>Windowsのファイルシステムに合わせて大文字小文字は無視する。</summary>
    [Fact]
    public void PinnedScene_PathComparisonIgnoresCase(){
        Assert.False(SceneAnalysisTargetPolicy.ShouldAlsoAnalyzePinnedScene(
            ActiveDocumentPath:@"C:\Game\MainScene.cs",PinnedSceneFilePath:@"c:\game\mainscene.cs"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PinnedScene_IsSkippedWhenNothingIsPinned(string?PinnedSceneFilePath){
        Assert.False(SceneAnalysisTargetPolicy.ShouldAlsoAnalyzePinnedScene(
            ActiveDocumentPath:@"C:\Game\Other.cs",PinnedSceneFilePath));
    }

    /// <summary>アクティブ文書が無くてもpin中Sceneは解析する(Designerだけ開いている状態)。</summary>
    [Fact]
    public void PinnedScene_IsAnalyzedWithoutAnActiveDocument(){
        Assert.True(SceneAnalysisTargetPolicy.ShouldAlsoAnalyzePinnedScene(
            ActiveDocumentPath:null,PinnedSceneFilePath:@"C:\Game\MainScene.cs"));
    }

    // ---- デバウンス ----

    /// <summary>保留が無い状態からの新規スケジュールは通常の待ち時間。</summary>
    [Fact]
    public void Debounce_UsesTheNormalDelayForAFreshSchedule(){
        Assert.Equal(
            SceneAnalysisDebouncePolicy.Delay,
            SceneAnalysisDebouncePolicy.ResolveDelay(TimeSpan.Zero));
    }

    /// <summary>
    /// 不具合の中心。VSの背景処理は対象Sceneと無関係な理由でも頻繁にworkspace変更を起こす。
    /// 張り直しのたびに待ち時間が丸ごとリセットされると、いつまでも解析が始まらない。
    /// </summary>
    [Fact]
    public void Debounce_FiresImmediatelyOnceTheCapIsReached(){
        Assert.Equal(
            TimeSpan.Zero,
            SceneAnalysisDebouncePolicy.ResolveDelay(SceneAnalysisDebouncePolicy.MaxDelay));
    }

    /// <summary>上限を跨がないよう、残り時間が通常の待ち時間より短ければ残りだけ待つ。</summary>
    [Fact]
    public void Debounce_NeverWaitsPastTheCap(){
        TimeSpan Elapsed=SceneAnalysisDebouncePolicy.MaxDelay-TimeSpan.FromMilliseconds(100);

        TimeSpan Resolved=SceneAnalysisDebouncePolicy.ResolveDelay(Elapsed);

        Assert.Equal(TimeSpan.FromMilliseconds(100),Resolved);
        Assert.True(Elapsed+Resolved<=SceneAnalysisDebouncePolicy.MaxDelay);
    }

    /// <summary>上限内なら通常の待ち時間のまま(まとめる効果を捨てない)。</summary>
    [Fact]
    public void Debounce_KeepsTheNormalDelayWellInsideTheCap(){
        Assert.Equal(
            SceneAnalysisDebouncePolicy.Delay,
            SceneAnalysisDebouncePolicy.ResolveDelay(TimeSpan.FromMilliseconds(50)));
    }

    /// <summary>
    /// Scene Designer自身の書き込み直後は、まとめる理由が無いので待たない
    /// (表示の追従とリネージ検証がこの解析待ちのため)。
    /// </summary>
    [Fact]
    public void Debounce_IsSkippedForOurOwnWrites(){
        Assert.Equal(
            TimeSpan.Zero,
            SceneAnalysisDebouncePolicy.ResolveDelay(TimeSpan.Zero,Immediate:true));
    }

    /// <summary>上限は通常の待ち時間より長いこと(そうでないとデバウンスが機能しない)。</summary>
    [Fact]
    public void Debounce_CapIsLongerThanTheNormalDelay(){
        Assert.True(SceneAnalysisDebouncePolicy.MaxDelay>SceneAnalysisDebouncePolicy.Delay);
    }
}
