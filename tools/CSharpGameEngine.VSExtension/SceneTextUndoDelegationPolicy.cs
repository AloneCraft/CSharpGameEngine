namespace CSharpGameEngine.VSExtension;

/// <summary>Ctrl+Z / Ctrl+Y をどこで処理するか。<see cref="SceneTextUndoDelegationPolicy"/>の結果。</summary>
internal enum SceneUndoRoute {
    /// <summary>何もしない(pin中Sceneが無い、または書き戻しが飛行中)。</summary>
    None,

    /// <summary><see cref="SceneEditDocument"/>のスタックから1件戻す/進める(メモリ内・即時)。</summary>
    InMemory,

    /// <summary>C#テキストのUndo履歴へ委譲する(<see cref="SceneWorkspaceService.TryUndo(string)"/>)。</summary>
    DelegateToText,
}

/// <summary>
/// M0051 Step 1-5: 実機報告「シーンビューの編集は取り消しも保存もできるが<b>保存した後も取り消しできる</b>
/// ようにして」への対応。Ctrl+Z / Ctrl+Y の行き先を決める純粋な判定。
///
/// <para>
/// <b>なぜ委譲が必要か(選択肢ではなく必然)</b>: 保存が着地すると
/// <see cref="SceneEditDocument.ResetTo"/>がUndo/Redoスタックを消すため、保存後のCtrl+Zはメモリ内には
/// 戻す先が無い。かといって履歴を保持し続けても解決しない —
/// <see cref="SceneEditDocument"/>は<b>逆操作を一切持たず</b>(クラスコメント参照)、
/// <see cref="SceneSourceEditor.ApplyForSaveSteps"/>は履歴を<b>前方向に再生</b>する設計なので、
/// 保存点より前へ戻すにはC#テキスト側の履歴を使うほか無い。ADR-0040の「C#が唯一の正本」とも一致する。
/// </para>
///
/// <para>
/// <b>委譲先を誤らないための説明文判定</b>: テキストUndo履歴には<b>ユーザーがコードエディタで手打ちした
/// 編集</b>も同じスタックに積まれている。無条件に委譲すると、Ctrl+Zを押し続けたときデザイナー由来でない
/// 編集まで巻き戻す。<see cref="SceneUndoTransactionNaming.IsSceneTransaction"/>で
/// 自分が積んだトランザクションであることを確かめてからだけ委譲する。
/// </para>
///
/// <para>
/// <b>VS標準のEdit.Undoとの二重発火について</b>: <c>GetKeyState</c>ポーリングはキー入力を消費しないため、
/// 理屈の上ではVSのEdit.Undoも同時に走りうる。しかしM0046の実機記録
/// (<c>Docs/Conformance.md</c>: ツールウィンドウがフォーカスを持つとツールバーの履歴ドロップダウンが空になり、
/// コードエディタのCtrl+Zが効かなくなる)は、<b>デザイナー面にフォーカスがある間はVSのEdit.Undoが不活性</b>で
/// あることを示している。委譲はデザイナー面にフォーカスがあるときにしか起きないので、二重発火の条件が揃わない。
/// 仮に実機で二重発火が観測されても、上記の説明文判定により2回目は「自分のものでない」として止まるため
/// 被害は1段に留まる。ここで<c>ITextUndoHistory</c>の深さをフレーム跨ぎで監視して補償する案は、
/// 「起きるか不明のバグ」を「タイミング依存で再現しないバグ」に変換するだけなので採らない。
/// </para>
/// </summary>
internal static class SceneTextUndoDelegationPolicy {
    /// <summary>
    /// <paramref name="CanUndoInMemory"/>は<see cref="SceneEditDocument.CanUndo"/>(Redo側なら
    /// <see cref="SceneEditDocument.CanRedo"/>)。<paramref name="HasPendingSourceWrite"/>のときは
    /// どちらにも落とさない — 書き戻し飛行中はメモリ内スタックとテキスト履歴が食い違う窓であり、
    /// 完了後に押し直せば正しく動く(M0038の判断を踏襲)。
    /// </summary>
    internal static SceneUndoRoute Decide(
        bool CanUndoInMemory,bool HasPendingSourceWrite,bool HasPinnedSnapshot){
        if(!HasPinnedSnapshot||HasPendingSourceWrite){
            return SceneUndoRoute.None;
        }

        return CanUndoInMemory?SceneUndoRoute.InMemory:SceneUndoRoute.DelegateToText;
    }

    /// <summary>
    /// <paramref name="LastTransactionDescription"/>は委譲先バッファの
    /// <c>ITextUndoHistory.LastUndoTransaction?.Description</c>(Redo側なら<c>LastRedoTransaction</c>)。
    /// 履歴が空ならnullが渡り、その場合は委譲しない。
    /// </summary>
    internal static bool ShouldDelegate(string?LastTransactionDescription)=>
        SceneUndoTransactionNaming.IsSceneTransaction(LastTransactionDescription);
}
