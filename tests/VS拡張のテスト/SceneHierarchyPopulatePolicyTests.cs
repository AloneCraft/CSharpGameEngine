using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-7: 実機報告「Hierarchyのインラインリネームが確定しない」の判定を固定する。
///
/// <para>
/// <c>Populate</c>は<c>SceneNodeViewModel.BuildWithSelection</c>でビューモデルを新規生成して
/// <c>ItemsSource</c>を差し替えるため、リネーム中のノードごと破棄され、Enterを受けるTextBoxが消える。
/// Step 1-6 Stage Aで再解析が確実に届くようになるまでは「解析が来ない/いつまでも遅延する」欠陥が
/// 偶然リネームを守っていた。
/// </para>
/// </summary>
public sealed class SceneHierarchyPopulatePolicyTests {
    private const string SceneSource="""
        using CSharpGameEngine.Entities;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[
                new Node { Name="Alpha" },
                new Node { Name="Beta" },
            ] };
        }
        """;

    // ---- 冪等化(Skip) ----

    /// <summary>
    /// VSの背景処理は対象Sceneと無関係な理由でも頻繁にworkspace変更を起こす。内容が同じなら
    /// 木を作り直さないことで、リネームに限らず<b>全ての一時状態</b>(ドラッグ中の掴み・選択・展開)が守られる。
    /// </summary>
    [Fact]
    public void UnchangedContent_IsSkippedEvenWhenNotRenaming(){
        Assert.Equal(
            HierarchyPopulateAction.Skip,
            HierarchyPopulatePolicy.Decide(IsInlineRenaming:false,ContentUnchanged:true));
    }

    [Fact]
    public void UnchangedContent_IsSkippedWhileRenaming(){
        Assert.Equal(
            HierarchyPopulateAction.Skip,
            HierarchyPopulatePolicy.Decide(IsInlineRenaming:true,ContentUnchanged:true));
    }

    // ---- 保留(Defer) ----

    /// <summary>不具合の中心。リネーム中は内容が変わっていても木を作り直さない。</summary>
    [Fact]
    public void ChangedContent_IsDeferredWhileRenaming(){
        Assert.Equal(
            HierarchyPopulateAction.Defer,
            HierarchyPopulatePolicy.Decide(IsInlineRenaming:true,ContentUnchanged:false));
    }

    [Fact]
    public void ChangedContent_IsAppliedWhenNotRenaming(){
        Assert.Equal(
            HierarchyPopulateAction.Apply,
            HierarchyPopulatePolicy.Decide(IsInlineRenaming:false,ContentUnchanged:false));
    }

    /// <summary>
    /// 保留中に対象ノードが消えたら、確定しても<c>TryGetId</c>が失敗するので先に取り消す。
    /// </summary>
    [Fact]
    public void MissingRenameTarget_CancelsTheRename(){
        Assert.True(HierarchyPopulatePolicy.ShouldCancelRenameOnRelease(RenamedPathStillExists:false));
        Assert.False(HierarchyPopulatePolicy.ShouldCancelRenameOnRelease(RenamedPathStillExists:true));
    }

    // ---- 内容署名 ----

    /// <summary>Scene未選択(null)とRootが無い状態は表示が違うので区別する。</summary>
    [Fact]
    public void Signature_DistinguishesNullModelFromMissingRoot(){
        var Model=SceneAnalysisTestHelper.Analyze([SceneSource]);

        Assert.NotEqual(HierarchyContentSignature.Compute(null),HierarchyContentSignature.Compute(Model));
    }

    [Fact]
    public void Signature_IsStableForTheSameContent(){
        Assert.Equal(
            HierarchyContentSignature.Compute(SceneAnalysisTestHelper.Analyze([SceneSource])),
            HierarchyContentSignature.Compute(SceneAnalysisTestHelper.Analyze([SceneSource])));
    }

    /// <summary>名前が変われば署名も変わる(リネーム確定後に必ず作り直されること)。</summary>
    [Fact]
    public void Signature_ChangesWhenANodeIsRenamed(){
        var Renamed=SceneAnalysisTestHelper.Analyze([SceneSource.Replace("\"Beta\"","\"BetaRenamed\"")]);

        Assert.NotEqual(
            HierarchyContentSignature.Compute(SceneAnalysisTestHelper.Analyze([SceneSource])),
            HierarchyContentSignature.Compute(Renamed));
    }

    /// <summary>構造が変われば署名も変わる。</summary>
    [Fact]
    public void Signature_ChangesWhenANodeIsAdded(){
        var WithExtra=SceneAnalysisTestHelper.Analyze([
                                                          SceneSource.Replace("new Node { Name=\"Beta\" },","new Node { Name=\"Beta\" },new Node { Name=\"Gamma\" },")]);

        Assert.NotEqual(
            HierarchyContentSignature.Compute(SceneAnalysisTestHelper.Analyze([SceneSource])),
            HierarchyContentSignature.Compute(WithExtra));
    }
}
