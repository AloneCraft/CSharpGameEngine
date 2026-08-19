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

    // ---- Ctrl+Z / Ctrl+Y の行き先(ウォーターマーク) ----
    //
    // 不変条件: 0 <= TextWatermark <= HistoryCount かつ
    // 「C#テキスト == History[0..TextWatermark) を再生した状態」。

    /// <summary>まだテキストへ書いていない編集を戻すだけなら、テキストは無関係で即時に完結する。</summary>
    [Fact]
    public void Undo_IsInMemoryOnlyForUnsavedEdits(){
        Assert.Equal(SceneUndoAction.InMemoryOnly,SceneTextWatermarkPolicy.DecideUndo(
            HistoryCount:3,TextWatermark:1,CanSyncText:false,IsSaving:false));
    }

    /// <summary>
    /// 実機報告「保存すると遅くなる」への対応の中心。保存済みの編集を戻すときも、
    /// メモリ内スタックが表示を即座に戻し、テキストUndoはその副作用として同期実行する。
    /// </summary>
    [Fact]
    public void Undo_SyncsTextWhenSteppingBelowTheWatermark(){
        Assert.Equal(SceneUndoAction.InMemoryWithText,SceneTextWatermarkPolicy.DecideUndo(
            HistoryCount:3,TextWatermark:3,CanSyncText:true,IsSaving:false));
    }

    /// <summary>
    /// ユーザーがコードエディタで直接C#を編集していたらテキストUndoは触れない。かといって
    /// メモリ内だけ戻すと「テキストがメモリより先に進んだ状態」になり不変条件が壊れる
    /// (ウォーターマークでは表現できない)ので、何もしないのが正しい。
    /// </summary>
    [Fact]
    public void Undo_DoesNothingWhenTextCannotFollow(){
        Assert.Equal(SceneUndoAction.None,SceneTextWatermarkPolicy.DecideUndo(
            HistoryCount:3,TextWatermark:3,CanSyncText:false,IsSaving:false));
    }

    [Fact]
    public void Undo_DoesNothingWithAnEmptyHistory(){
        Assert.Equal(SceneUndoAction.None,SceneTextWatermarkPolicy.DecideUndo(
            HistoryCount:0,TextWatermark:0,CanSyncText:true,IsSaving:false));
    }

    /// <summary>書き戻し飛行中はメモリ内とテキストが食い違う窓。完了後の押し直しで正しく動く。</summary>
    [Fact]
    public void Undo_DoesNothingWhileASaveIsInFlight(){
        Assert.Equal(SceneUndoAction.None,SceneTextWatermarkPolicy.DecideUndo(
            HistoryCount:3,TextWatermark:1,CanSyncText:true,IsSaving:true));
    }

    [Fact]
    public void Redo_IsInMemoryOnlyWhileAheadOfTheWatermark(){
        Assert.Equal(SceneUndoAction.InMemoryOnly,SceneTextWatermarkPolicy.DecideRedo(
            RedoCount:1,HistoryCount:3,TextWatermark:1,CanSyncText:false,IsSaving:false));
    }

    [Fact]
    public void Redo_SyncsTextWhenTheTextHasOurUnitToRestore(){
        Assert.Equal(SceneUndoAction.InMemoryWithText,SceneTextWatermarkPolicy.DecideRedo(
            RedoCount:1,HistoryCount:2,TextWatermark:2,CanSyncText:true,IsSaving:false));
    }

    /// <summary>
    /// Undoと非対称に、テキストが追随できなくてもRedoは進める。メモリがテキストより<b>先</b>へ出る
    /// 状態は「まだ保存していない編集がある」そのものでウォーターマークが表現できる。
    /// この非対称が無いと、メモリ内だけ戻した編集をRedoできなくなる。
    /// </summary>
    [Fact]
    public void Redo_ProceedsInMemoryWhenTextCannotFollow(){
        Assert.Equal(SceneUndoAction.InMemoryOnly,SceneTextWatermarkPolicy.DecideRedo(
            RedoCount:1,HistoryCount:2,TextWatermark:2,CanSyncText:false,IsSaving:false));
    }

    [Fact]
    public void Redo_DoesNothingWithAnEmptyRedoStack(){
        Assert.Equal(SceneUndoAction.None,SceneTextWatermarkPolicy.DecideRedo(
            RedoCount:0,HistoryCount:2,TextWatermark:2,CanSyncText:true,IsSaving:false));
    }

    // ---- 保存が書き出す範囲 ----

    [Fact]
    public void Save_WritesTheSuffixAboveTheWatermark(){
        Assert.Equal(
            SceneSaveSuffixKind.WriteSuffix,SceneSaveSuffixPolicy.Decide(HistoryCount:5,TextWatermark:2));
    }

    /// <summary>
    /// これが無いと保存が壊れる。ウォーターマークより下へ潜った状態でCtrl+Sすると部分列が空になり、
    /// ApplyForSaveSteps が FinalRoot=null を返して「保存に失敗しました」と出る。
    /// 潜った直後はテキストも一緒に戻っているので、実際には既に同期済みで何もしないのが正しい。
    /// </summary>
    [Fact]
    public void Save_DoesNothingWhenTheTextIsAlreadyAtTheSamePosition(){
        Assert.Equal(
            SceneSaveSuffixKind.AlreadyInSync,SceneSaveSuffixPolicy.Decide(HistoryCount:2,TextWatermark:2));
    }

    // ---- テキスト同期の所有判定 ----
    //
    // Step 1-5 は ITextUndoHistory.LastUndoTransaction.Description の接頭辞で判定していたが、
    // VSの実装(VsUndoHistory)はスタックを覗く系のメンバーを実装しておらず NotSupportedException を
    // 投げる。バッファ版番号(単調増加)の一致で「以降だれも触っていない」ことを確かめる。

    [Fact]
    public void TextSync_IsAllowedRightAfterOurOwnWrite(){
        SceneTextSyncState State=SceneTextSyncPolicy.AfterWrite(VersionAfterWrite:8);

        Assert.True(SceneTextSyncPolicy.CanUndoText(State,CurrentBufferVersion:8));
    }

    /// <summary>
    /// 所有判定の核心。保存のあとユーザーがコードエディタで手打ちすると版番号が進む。
    /// このままUndo(1)するとスタックの一番上にあるユーザーの編集を巻き戻してしまう。
    /// </summary>
    [Fact]
    public void TextSync_IsRefusedAfterSomeoneElseTouchedTheBuffer(){
        SceneTextSyncState State=SceneTextSyncPolicy.AfterWrite(VersionAfterWrite:8);

        Assert.False(SceneTextSyncPolicy.CanUndoText(State,CurrentBufferVersion:9));
    }

    /// <summary>一度も書き出していなければ、テキスト側に自分のものは無い。</summary>
    [Fact]
    public void TextSync_IsRefusedBeforeAnyWrite(){
        Assert.False(SceneTextSyncPolicy.CanUndoText(SceneTextSyncPolicy.Empty,CurrentBufferVersion:1));
        Assert.False(SceneTextSyncPolicy.CanRedoText(SceneTextSyncPolicy.Empty,CurrentBufferVersion:1));
    }

    /// <summary>
    /// M0051 Step 1-7: 既定値の回帰。<c>Dictionary.TryGetValue</c>のout既定は
    /// <c>BufferVersion=0</c>で、版番号0のバッファと偶然一致しうる。
    /// <see cref="SceneTextSyncPolicy.Empty"/>は必ず-1で、どの実在の版番号とも一致しないこと。
    /// </summary>
    [Fact]
    public void TextSync_EmptyNeverMatchesARealBufferVersion(){
        Assert.Equal(-1,SceneTextSyncPolicy.Empty.BufferVersion);
        Assert.False(SceneTextSyncPolicy.CanUndoText(SceneTextSyncPolicy.Empty,CurrentBufferVersion:0));
        Assert.False(SceneTextSyncPolicy.CanUndoText(default,CurrentBufferVersion:1));
    }

    // ---- 着地待ちフラグの消費 ----

    /// <summary>
    /// M0051 Step 1-7: 以前は再解析イベントの先頭で無条件に消費していたため、対象Sceneのモデルが
    /// 載っていないイベントでもフラグが消えた。消費されると本当の着地が外部変更として扱われ、
    /// 保存直後は<c>IsDirty==false</c>なので<c>FollowSilently</c>→<c>ResetTo</c>で
    /// ウォーターマークと履歴が飛ぶ。
    /// </summary>
    [Fact]
    public void AwaitingKey_IsNotConsumedByAnUnrelatedEvent(){
        SceneKey Key=new("project-a","Game.MainScene");

        Assert.False(SceneOwnSaveLandingPolicy.ShouldConsumeAwaitingKey(Key,EventContainsTargetScene:false));
        Assert.True(SceneOwnSaveLandingPolicy.ShouldConsumeAwaitingKey(Key,EventContainsTargetScene:true));
    }

    [Fact]
    public void AwaitingKey_IsNotConsumedWhenNothingIsAwaited(){
        Assert.False(SceneOwnSaveLandingPolicy.ShouldConsumeAwaitingKey(null,EventContainsTargetScene:true));
    }

    /// <summary>Undoした分だけテキスト側のRedoが可能になり、Redoで戻る。</summary>
    [Fact]
    public void TextSync_MovesUnitsBetweenUndoAndRedo(){
        SceneTextSyncState State=SceneTextSyncPolicy.AfterWrite(VersionAfterWrite:6);
        Assert.False(SceneTextSyncPolicy.CanRedoText(State,CurrentBufferVersion:6));

        State=SceneTextSyncPolicy.AfterUndo(State,VersionAfterUndo:7);
        Assert.Equal(1,State.RedoableUnits);
        Assert.True(SceneTextSyncPolicy.CanRedoText(State,CurrentBufferVersion:7));

        State=SceneTextSyncPolicy.AfterRedo(State,VersionAfterRedo:8);
        Assert.Equal(0,State.RedoableUnits);
        Assert.False(SceneTextSyncPolicy.CanRedoText(State,CurrentBufferVersion:8));
    }

    /// <summary>
    /// これを忘れると乖離する。テキストに触れない新しい編集が積まれるとDocumentのRedoStackは
    /// 捨てられるので、テキスト側に残っている自分のRedo単位はもう対応物が無い。
    /// 無効化しないと、メモリ内だけ戻した編集をRedoしたときに無関係な古いテキストRedoが走る。
    /// </summary>
    [Fact]
    public void TextSync_DropsStaleRedoUnitsOnANewInMemoryEdit(){
        SceneTextSyncState State=SceneTextSyncPolicy.AfterWrite(VersionAfterWrite:6);
        State=SceneTextSyncPolicy.AfterUndo(State,VersionAfterUndo:7);
        Assert.Equal(1,State.RedoableUnits);

        State=SceneTextSyncPolicy.AfterInMemoryEdit(State);

        Assert.Equal(0,State.RedoableUnits);
        Assert.False(SceneTextSyncPolicy.CanRedoText(State,CurrentBufferVersion:7));
        // 自分はバッファを触っていないので、Undo側の所有判定は保つ。
        Assert.True(SceneTextSyncPolicy.CanUndoText(State,CurrentBufferVersion:7));
    }

    /// <summary>新しい書き込みはテキスト側のRedoスタックを捨てる(通常のUndoスタックの規則)。</summary>
    [Fact]
    public void TextSync_ClearsRedoOnANewWrite(){
        SceneTextSyncState State=SceneTextSyncPolicy.AfterWrite(VersionAfterWrite:6);
        State=SceneTextSyncPolicy.AfterUndo(State,VersionAfterUndo:7);

        State=SceneTextSyncPolicy.AfterWrite(VersionAfterWrite:8);

        Assert.Equal(0,State.RedoableUnits);
    }

    [Fact]
    public void TransactionName_CarriesTheOperationLabel(){
        string Formatted=SceneUndoTransactionNaming.Format("Remove node");

        Assert.StartsWith(SceneUndoTransactionNaming.Prefix,Formatted,System.StringComparison.Ordinal);
        Assert.EndsWith("Remove node",Formatted,System.StringComparison.Ordinal);
    }
}
