namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-4: 実機報告「ヒエラルキーで編集してもctrl+sで取り消される」の原因となった
/// 2つの判定を純粋関数として固定する。
/// </summary>
public sealed class SceneSavePolicyTests {
    // ---- Ctrl+Sをどこで引き受けるか ----

    /// <summary>
    /// 不具合の中心。以前はビューポートのフォーカスだけを条件にしていたため、Hierarchyで編集した
    /// 直後のCtrl+Sが保存へ届かなかった。補助サーフェス(Hierarchy/Inspector)でも引き受ける。
    /// </summary>
    [Fact]
    public void Save_IsAcceptedFromAuxiliarySurfacesToo(){
        Assert.True(SceneSaveTriggerPolicy.ShouldSave(
            AnyDesignerSurfaceHasFocus:true,HasPinnedSnapshot:true,IsSaving:false));
    }

    [Fact]
    public void Save_IsRejectedWhenNoDesignerSurfaceHasFocus(){
        Assert.False(SceneSaveTriggerPolicy.ShouldSave(
            AnyDesignerSurfaceHasFocus:false,HasPinnedSnapshot:true,IsSaving:false));
    }

    [Fact]
    public void Save_IsRejectedWithoutAPinnedScene(){
        Assert.False(SceneSaveTriggerPolicy.ShouldSave(
            AnyDesignerSurfaceHasFocus:true,HasPinnedSnapshot:false,IsSaving:false));
    }

    /// <summary>Ctrl+Sの押しっぱなしや連打で保存が多重起動しないこと。</summary>
    [Fact]
    public void Save_IsRejectedWhileAnotherSaveIsInFlight(){
        Assert.False(SceneSaveTriggerPolicy.ShouldSave(
            AnyDesignerSurfaceHasFocus:true,HasPinnedSnapshot:true,IsSaving:true));
    }

    // ---- 再解析モデルをHubへ流してよいか ----

    /// <summary>
    /// 不具合の実体。pin中Sceneに未保存の編集がある状態で、同じSceneの再解析結果(未編集のC#由来)を
    /// 流すとHierarchyが古い内容で再構築され、編集が消えたように見える。
    /// </summary>
    [Fact]
    public void AnalyzedModel_IsNotPublishedOverUnsavedEditsOfTheSameScene(){
        Assert.False(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:"Game.MainScene",
            PinnedClassFullyQualifiedName:"Game.MainScene",
            PinnedDocumentHasUnsavedEdits:true));
    }

    /// <summary>
    /// 未保存の編集が無ければ、実C#の内容がそのまま正しいので流してよい
    /// (保存直後や外部編集の追従がここを通る)。
    /// </summary>
    [Fact]
    public void AnalyzedModel_IsPublishedWhenThereAreNoUnsavedEdits(){
        Assert.True(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:"Game.MainScene",
            PinnedClassFullyQualifiedName:"Game.MainScene",
            PinnedDocumentHasUnsavedEdits:false));
    }

    /// <summary>
    /// M0053: pin中は別Sceneの再解析も流さない。Hierarchyとシーン デザイナーが別のSceneを指す状態を
    /// 作らないため(選択・Inspector・ギズモの整合が崩れる)。別Sceneへ移るのは明示的なpin切替だけ。
    /// </summary>
    [Fact]
    public void AnalyzedModel_ForADifferentScene_DoesNotReplaceThePinnedOne(){
        Assert.False(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:"Game.OtherScene",
            PinnedClassFullyQualifiedName:"Game.MainScene",
            PinnedDocumentHasUnsavedEdits:true));
        Assert.False(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:"Game.OtherScene",
            PinnedClassFullyQualifiedName:"Game.MainScene",
            PinnedDocumentHasUnsavedEdits:false));
    }

    [Fact]
    public void AnalyzedModel_IsPublishedWhenNothingIsPinned(){
        Assert.True(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:"Game.MainScene",
            PinnedClassFullyQualifiedName:null,
            PinnedDocumentHasUnsavedEdits:true));
    }

    /// <summary>
    /// 解析対象が無くなった(nullの)通知も、未保存編集があるpin中Sceneを消してはならない
    /// (アクティブ文書が一時的に別ファイルへ移っただけでHierarchyが空になってしまうため)。
    /// </summary>
    [Fact]
    public void NullAnalyzedModel_DoesNotClearAPinnedSceneWithUnsavedEdits(){
        Assert.False(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:null,
            PinnedClassFullyQualifiedName:"Game.MainScene",
            PinnedDocumentHasUnsavedEdits:true));
    }

    /// <summary>
    /// M0053の直接の回帰テスト。実機報告「シーン デザイナーで開いているのにヒエラルキーが表示できない」の実体。
    ///
    /// <para>
    /// <c>SceneWorkspaceService</c>は文書切替のたびに無条件で<c>ModelChanged(null)</c>を発行する。
    /// 以前は未編集ならこのnullを流していたため、Sceneを開いた直後にSceneを宣言しない.csタブへ
    /// 切り替えるだけでHierarchyが空になっていた。<b>編集の有無は関係ない</b> — pin中である限り塞ぐ。
    /// </para>
    /// </summary>
    [Fact]
    public void NullAnalyzedModel_NeverClearsAPinnedSceneEvenWithoutUnsavedEdits(){
        Assert.False(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:null,
            PinnedClassFullyQualifiedName:"Game.MainScene",
            PinnedDocumentHasUnsavedEdits:false));
    }

    /// <summary>未pinなら別Sceneの解析も従来どおり流す(アクティブ文書への追従は未pin時だけ残る)。</summary>
    [Fact]
    public void AnalyzedModel_OfADifferentSceneIsPublishedWhenNothingIsPinned(){
        Assert.True(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:"Game.OtherScene",
            PinnedClassFullyQualifiedName:null,
            PinnedDocumentHasUnsavedEdits:false));
    }

    /// <summary>
    /// 実機報告そのものを名前で固定する: シーン デザイナーで開いた直後(未編集)に、
    /// Sceneを宣言しない.csタブをアクティブにしてもHierarchyは消えない。
    /// </summary>
    [Fact]
    public void ActivatingANonSceneDocument_KeepsThePinnedHierarchy(){
        Assert.False(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:null,
            PinnedClassFullyQualifiedName:"UserEcsProject.Scenes.FirstPersonStageScene",
            PinnedDocumentHasUnsavedEdits:false));
    }

    /// <summary>未pinならnullの通知はそのまま流す(通常のクリア動作が生きていること)。</summary>
    [Fact]
    public void NullAnalyzedModel_IsPublishedWhenNothingIsPinned(){
        Assert.True(SceneHubPublicationPolicy.ShouldPublishAnalyzedModel(
            AnalyzedClassFullyQualifiedName:null,
            PinnedClassFullyQualifiedName:null,
            PinnedDocumentHasUnsavedEdits:false));
    }

}
