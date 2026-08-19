namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-5: 実機報告「ヒエラルキーは取り消しできない」「保存した後も取り消しできるようにして」の
/// 原因となった判定を純粋関数として固定する。
/// </summary>
public sealed class SceneUndoRoutingPolicyTests {
    // ---- Ctrl+Z / Ctrl+Y / Ctrl+S をどこで引き受けるか ----

    [Fact]
    public void Key_IsRoutedWhenTheViewportHasFocus(){
        Assert.True(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused:true,AuxiliarySurfaceFocused:false,TextEntryFocused:false));
    }

    /// <summary>
    /// 不具合の中心。Hierarchyで編集した直後はフォーカスがHierarchy側にあり、
    /// ビューポート限定の門番だとCtrl+Zがどこにも届かなかった。
    /// </summary>
    [Fact]
    public void Key_IsRoutedWhenAnAuxiliarySurfaceHasFocus(){
        Assert.True(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:true,TextEntryFocused:false));
    }

    [Fact]
    public void Key_IsNotRoutedWhenNoDesignerSurfaceHasFocus(){
        Assert.False(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:false,TextEntryFocused:false));
    }

    /// <summary>
    /// 門番を広げたことで生まれる退行の本命。Hierarchyのインラインリネームも
    /// Inspectorの数値入力もTextBoxで、IsKeyboardFocusWithinは入力中もtrueになる。
    /// ここを除外しないと、リネーム途中のCtrl+Zが「文字の取り消し」ではなく
    /// 「シーン全体のUndo」になってしまう。
    /// </summary>
    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void UndoRedo_IsNotRoutedWhileTypingInATextBox(bool Viewport,bool Auxiliary){
        Assert.False(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            Viewport,Auxiliary,TextEntryFocused:true));
    }

    /// <summary>
    /// 一方でCtrl+Sはテキスト入力中でも引き受ける。TextBoxに「保存」の意味は無く、ここで降りると
    /// VS標準の保存が未反映の.csをそのまま保存してしまい、Step 1-4で直した不具合へ戻る。
    /// </summary>
    [Fact]
    public void Save_IsStillRoutedWhileTypingInATextBox(){
        Assert.True(SceneDesignerInputRoutingPolicy.ShouldRouteSaveToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:true));
    }

    [Fact]
    public void Save_IsNotRoutedWhenNoDesignerSurfaceHasFocus(){
        Assert.False(SceneDesignerInputRoutingPolicy.ShouldRouteSaveToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:false));
    }

    // ---- メモリ内Undoかテキスト委譲か ----

    /// <summary>未反映の編集が残っている間は、メモリ内スタックを1件ずつ消化する(即時・再解析待ち無し)。</summary>
    [Fact]
    public void Route_IsInMemoryWhileUnsavedEditsRemain(){
        Assert.Equal(SceneUndoRoute.InMemory,SceneTextUndoDelegationPolicy.Decide(
            CanUndoInMemory:true,HasPendingSourceWrite:false,HasPinnedSnapshot:true));
    }

    /// <summary>
    /// 「保存した後も取り消しできるようにして」の実体。保存が着地するとResetToがスタックを消すため、
    /// メモリ内には戻す先が無い。ここでテキストUndoへ降りられないと完全に無反応になる。
    /// </summary>
    [Fact]
    public void Route_DelegatesToTextWhenTheInMemoryStackIsEmpty(){
        Assert.Equal(SceneUndoRoute.DelegateToText,SceneTextUndoDelegationPolicy.Decide(
            CanUndoInMemory:false,HasPendingSourceWrite:false,HasPinnedSnapshot:true));
    }

    [Fact]
    public void Route_IsNoneWithoutAPinnedScene(){
        Assert.Equal(SceneUndoRoute.None,SceneTextUndoDelegationPolicy.Decide(
            CanUndoInMemory:true,HasPendingSourceWrite:false,HasPinnedSnapshot:false));
    }

    /// <summary>
    /// 書き戻し飛行中はメモリ内スタックとテキスト履歴が食い違う窓。どちらにも落とさない
    /// (完了後の押し直しで正しく動く)。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Route_IsNoneWhileASourceWriteIsInFlight(bool CanUndoInMemory){
        Assert.Equal(SceneUndoRoute.None,SceneTextUndoDelegationPolicy.Decide(
            CanUndoInMemory,HasPendingSourceWrite:true,HasPinnedSnapshot:true));
    }

    // ---- 委譲先が自分のトランザクションか ----

    [Fact]
    public void Delegation_IsAllowedForOurOwnTransaction(){
        Assert.True(SceneTextUndoDelegationPolicy.ShouldDelegate(
            SceneUndoTransactionNaming.Format("Rename node")));
    }

    /// <summary>
    /// テキスト履歴にはユーザーがコードエディタで手打ちした編集も積まれている。区別せず委譲すると
    /// Ctrl+Zを押し続けたときデザイナー由来でない編集まで巻き戻してしまう。
    /// </summary>
    [Fact]
    public void Delegation_IsRefusedForSomeoneElsesTransaction(){
        Assert.False(SceneTextUndoDelegationPolicy.ShouldDelegate("Typing"));
    }

    /// <summary>履歴が空(次にUndoするものが無い)ならnullが渡る。</summary>
    [Fact]
    public void Delegation_IsRefusedWhenTheTextHistoryIsEmpty(){
        Assert.False(SceneTextUndoDelegationPolicy.ShouldDelegate(null));
    }

    [Fact]
    public void TransactionName_RoundTrips(){
        string Formatted=SceneUndoTransactionNaming.Format("Remove node");

        Assert.True(SceneUndoTransactionNaming.IsSceneTransaction(Formatted));
        Assert.EndsWith("Remove node",Formatted,System.StringComparison.Ordinal);
    }

    /// <summary>接頭辞の一部だけ一致する説明文を誤って自分のものと判定しないこと。</summary>
    [Theory]
    [InlineData("Scene")]
    [InlineData("Scene の保存")]
    [InlineData("デザイナー: Rename node")]
    public void TransactionName_DoesNotMatchNearMisses(string Description){
        Assert.False(SceneUndoTransactionNaming.IsSceneTransaction(Description));
    }
}
