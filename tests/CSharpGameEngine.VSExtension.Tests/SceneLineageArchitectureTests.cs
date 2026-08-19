using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-7: <b>リネージ判定に<c>SceneDiffEngine</c>を使わない</b>という制約をコードで表現する。
///
/// <para>
/// Step 1-6は<c>SceneDiffEngine.Compare(Document.Projection,RealProjection).IsEmpty</c>で
/// リネージ一致を判定した。<see cref="SceneDiffEngine"/>は<b>Rendererの増分更新分類</b>が目的で
/// Componentの<c>EmitExpressionText</c>をOrdinal比較するが、
/// <see cref="SceneModelEditApplier"/>の予測モデルはその式テキストを古いまま引き継ぐ
/// (<c>SceneModelEditApplierTests</c>が「<c>ComponentChanged</c>のみ許容」として明文化)。
/// そのため実機で<b>一度も</b><see cref="SceneLandingAction.KeepDocument"/>にならなかった。
/// </para>
///
/// <para>
/// この失敗は「純粋関数のテストは通るが引数の作り方が誤っている」型で、判定側のユニットテストでは
/// 捕まえられない。<c>SceneLandingDecisionTests</c>が実解析との突き合わせで振る舞いを固定し、
/// こちらは<b>実装が元の形へ逆戻りしないこと</b>を固定する。
/// </para>
/// </summary>
public sealed class SceneLineageArchitectureTests {
    /// <summary>
    /// リネージ判定は<c>SceneDiffEngine</c>に依存してはならない。
    /// 「単純化」でここへ戻すと、実機で高速経路が丸ごと死ぬ。
    /// </summary>
    [Fact]
    public void LineageDecision_DoesNotDependOnSceneDiffEngine(){
        string Source=ReadSource("SceneLandingDecision.cs");

        Assert.DoesNotContain("SceneDiffEngine",Source,StringComparison.Ordinal);
    }

    /// <summary>
    /// セッション層で<b>予測投影(Document)を第1引数にした</b><c>SceneDiffEngine.Compare</c>を
    /// 等価判定に使ってはならない。
    ///
    /// <para>
    /// <c>Compare</c>自体は禁止しない — <c>BuildSnapshot</c>のRenderer差分
    /// (<c>Compare(Previous,Document.Projection)</c>)と、実投影同士の比較
    /// (<c>RealContentActuallyChanged</c>)は本来の用途で正しい。
    /// 禁じるのは<b>予測と実解析を突き合わせて「同じか」を判断する</b>形。
    /// </para>
    /// </summary>
    [Fact]
    public void SessionService_DoesNotComparePredictedProjectionAgainstAnalysis(){
        string Source=ReadSource("SceneEditorSessionService.cs");

        Assert.DoesNotContain("SceneDiffEngine.Compare(Document.",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("SceneDiffEngine.Compare(this.Documents",Source,StringComparison.Ordinal);
    }

    /// <summary>着地の判定が実際に<see cref="SceneLandingDecision"/>を経由していること。</summary>
    [Fact]
    public void SessionService_RoutesOwnLandingsThroughLandingDecision(){
        string Source=ReadSource("SceneEditorSessionService.cs");

        Assert.Contains("SceneLandingDecision.Evaluate(",Source,StringComparison.Ordinal);
    }

    // ---- M0051 Step 1-8: デバウンス状態の管理 ----

    /// <summary>
    /// デバウンスの保留窓は<b>必ずポリシー経由で</b>遷移させる。
    ///
    /// <para>
    /// Step 1-6は待ち時間の計算だけを純粋関数にし、状態の管理を呼び出し側へ残した。その結果
    /// 窓を「解析の完走時にしか閉じない」という誤りが入り、解析が追い越され続けると
    /// 待ち時間が恒久的に0へ縮退して<b>どの解析も完走しなくなった</b>
    /// (実機で右クリックメニューが「シーンを解析中」でグレーアウトし、VS再起動まで直らなかった)。
    /// 直接代入へ戻すと同じ穴が開く。
    /// </para>
    /// </summary>
    [Fact]
    public void WorkspaceService_MutatesDebounceStateOnlyThroughThePolicy(){
        string Source=ReadSource("SceneWorkspaceService.cs");

        // 状態を直接組み立てるのが唯一の抜け道。ポリシーの戻り値を代入するのは正しい形なので、
        // 代入の右辺そのものではなく「その場で状態を作っていないか」を見る。
        Assert.DoesNotContain("new SceneAnalysisDebounceState(",Source,StringComparison.Ordinal);
    }

    /// <summary>
    /// 窓を閉じるのは<b>解析の開始</b>であって完走ではない。<c>StartAnalysis</c>から
    /// <c>OnStarted</c>が消えると、この不具合がそのまま戻る。
    /// </summary>
    [Fact]
    public void WorkspaceService_ClosesTheDebounceWindowWhenAnalysisStarts(){
        string Source=ReadSource("SceneWorkspaceService.cs");

        Assert.Contains("SceneAnalysisDebouncePolicy.OnStarted(",Source,StringComparison.Ordinal);
        Assert.Contains("SceneAnalysisDebouncePolicy.OnScheduled(",Source,StringComparison.Ordinal);
    }

    /// <summary>
    /// コメントを落として<b>実際のコードだけ</b>を返す。これらのファイルのdocコメントは
    /// 「なぜ<c>SceneDiffEngine</c>を使わないか」を説明しているので、素の文字列検索だと
    /// 説明文そのものに反応してしまう。
    /// </summary>
    private static string ReadSource(string FileName){
        string[]Lines=File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory,"TestAssets","Source",FileName));
        return string.Concat(
            Lines.Where(static Line=>!Line.TrimStart().StartsWith("//",StringComparison.Ordinal)));
    }
}
