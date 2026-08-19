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

    // ---- 委譲先が自分のトランザクションか(台帳判定) ----
    //
    // 当初は ITextUndoHistory.LastUndoTransaction.Description の接頭辞で判定していたが、VSの実装
    // (VsUndoHistory)はスタックを覗く系のメンバーを実装しておらず NotSupportedException を投げる。
    // 実機ではCtrl+Zのたびに CompositionTarget.Rendering のハンドラごと落ちた。
    // 代わりに「自分が積んだ単位数」と「自分が最後に触った直後のバッファ版番号」で判定する。

    /// <summary>保存で積んだ直後は、積んだ数だけ委譲できる。</summary>
    [Fact]
    public void Ledger_AllowsDelegationForTheUnitsWeJustWrote(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:8,UnitsWritten:3);

        Assert.Equal(3,Entry.UndoableUnits);
        Assert.Equal(0,Entry.RedoableUnits);
        Assert.True(SceneTextUndoLedgerPolicy.CanDelegateUndo(Entry,CurrentBufferVersion:8));
    }

    /// <summary>
    /// 所有判定の核心。保存のあとユーザーがコードエディタで手打ちすると版番号が進む。
    /// このとき委譲するとスタックの一番上にあるユーザーの編集を巻き戻してしまう。
    /// </summary>
    [Fact]
    public void Ledger_RefusesDelegationAfterSomeoneElseTouchedTheBuffer(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:8,UnitsWritten:3);

        Assert.False(SceneTextUndoLedgerPolicy.CanDelegateUndo(Entry,CurrentBufferVersion:9));
    }

    /// <summary>自分の単位を使い切ったら、その下にあるユーザーの編集へは降りない。</summary>
    [Fact]
    public void Ledger_StopsOnceOurOwnUnitsAreExhausted(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:6,UnitsWritten:2);

        Entry=SceneTextUndoLedgerPolicy.AfterUndo(Entry,VersionAfterUndo:7);
        Assert.True(SceneTextUndoLedgerPolicy.CanDelegateUndo(Entry,CurrentBufferVersion:7));

        Entry=SceneTextUndoLedgerPolicy.AfterUndo(Entry,VersionAfterUndo:8);
        Assert.Equal(0,Entry.UndoableUnits);
        Assert.False(SceneTextUndoLedgerPolicy.CanDelegateUndo(Entry,CurrentBufferVersion:8));
    }

    /// <summary>Undoした分はRedoで戻せる(委譲Undo後のCtrl+Yが片手落ちにならないこと)。</summary>
    [Fact]
    public void Ledger_MovesUnitsBetweenUndoAndRedo(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:6,UnitsWritten:2);
        Assert.False(SceneTextUndoLedgerPolicy.CanDelegateRedo(Entry,CurrentBufferVersion:6));

        Entry=SceneTextUndoLedgerPolicy.AfterUndo(Entry,VersionAfterUndo:7);

        Assert.True(SceneTextUndoLedgerPolicy.CanDelegateRedo(Entry,CurrentBufferVersion:7));
        Assert.Equal(1,Entry.RedoableUnits);

        Entry=SceneTextUndoLedgerPolicy.AfterRedo(Entry,VersionAfterRedo:8);

        Assert.Equal(2,Entry.UndoableUnits);
        Assert.Equal(0,Entry.RedoableUnits);
    }

    /// <summary>
    /// 保存を2回続けたら単位は累積する(1回目の保存分まで遡って取り消せる)。
    /// </summary>
    [Fact]
    public void Ledger_AccumulatesAcrossConsecutiveSaves(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:6,UnitsWritten:2);
        Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            Entry,VersionBeforeWrite:6,VersionAfterWrite:9,UnitsWritten:3);

        Assert.Equal(5,Entry.UndoableUnits);
    }

    /// <summary>
    /// ただし保存と保存の間にユーザーが手打ちしていたら累積しない。古い単位はユーザーの編集の
    /// 下に埋まっており、通り抜けて到達してはならない。
    /// </summary>
    [Fact]
    public void Ledger_DoesNotAccumulateAcrossAForeignEdit(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:6,UnitsWritten:2);
        Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            Entry,VersionBeforeWrite:11,VersionAfterWrite:13,UnitsWritten:3);

        Assert.Equal(3,Entry.UndoableUnits);
    }

    /// <summary>新しい編集を積めばRedoスタックは消える(通常のUndoスタックの規則)。</summary>
    [Fact]
    public void Ledger_ClearsRedoOnANewWrite(){
        SceneTextUndoLedgerEntry Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            SceneTextUndoLedgerPolicy.Empty,VersionBeforeWrite:5,VersionAfterWrite:6,UnitsWritten:1);
        Entry=SceneTextUndoLedgerPolicy.AfterUndo(Entry,VersionAfterUndo:7);
        Assert.Equal(1,Entry.RedoableUnits);

        Entry=SceneTextUndoLedgerPolicy.AfterWrite(
            Entry,VersionBeforeWrite:7,VersionAfterWrite:8,UnitsWritten:1);

        Assert.Equal(0,Entry.RedoableUnits);
    }

    /// <summary>保存を一度もしていなければ委譲しない(台帳が空のまま)。</summary>
    [Fact]
    public void Ledger_RefusesDelegationBeforeAnySave(){
        Assert.False(SceneTextUndoLedgerPolicy.CanDelegateUndo(
            SceneTextUndoLedgerPolicy.Empty,CurrentBufferVersion:1));
        Assert.False(SceneTextUndoLedgerPolicy.CanDelegateRedo(
            SceneTextUndoLedgerPolicy.Empty,CurrentBufferVersion:1));
    }

    [Fact]
    public void TransactionName_CarriesTheOperationLabel(){
        string Formatted=SceneUndoTransactionNaming.Format("Remove node");

        Assert.StartsWith(SceneUndoTransactionNaming.Prefix,Formatted,System.StringComparison.Ordinal);
        Assert.EndsWith("Remove node",Formatted,System.StringComparison.Ordinal);
    }
}
