using System;
using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>(Model, Projection)の対。<see cref="SceneEditDocument"/>のUndo/Redoが丸ごとスナップショットする単位。</summary>
internal sealed record SceneEditSnapshot(SceneModel Model,SceneProjection Projection);

/// <summary>1回の<see cref="SceneEditDocument.TryApply"/>で積まれるUndo単位。</summary>
internal sealed class SceneUndoEntry {
    internal SceneUndoEntry(
        SceneEditSnapshot Before,SceneEditSnapshot After,IReadOnlyList<SceneEditOperation>Operations,string Label){
        this.Before=Before;
        this.After=After;
        this.Operations=Operations;
        this.Label=Label;
    }

    internal SceneEditSnapshot Before { get; }

    internal SceneEditSnapshot After { get; }

    internal IReadOnlyList<SceneEditOperation>Operations { get; }

    internal string Label { get; }
}

/// <summary>
/// M0047: 編集セッション中の正本。<see cref="SceneModel"/>がimmutableであることを使い、Undo/Redoは
/// 差分やインバース操作を計算せず、(Model,Projection)スナップショットの列をそのまま保持する
/// (シーンは小規模で、構造共有によりスナップショットの実コストは小さい)。
///
/// <para>
/// C#ソースへは自分からは一切書き込まない。書き込みは呼び出し側が保存操作のタイミングで
/// <see cref="UnwrittenHistory"/>を<see cref="SceneSourceEditor.ApplyForSaveSteps"/>へ渡し、
/// 履歴エントリ1件=VS Undoトランザクション1件として書き戻す(呼び出し側の責務)。
/// </para>
///
/// <para>
/// M0051 Step 1-6: <b>履歴はセッション全体で保持する</b>。保存しても捨てない。
/// どこまでテキストへ書き出したかは<see cref="TextWatermark"/>が持ち、
/// <see cref="IsDirty"/>もそこからの距離で決まる。以前は保存の着地で
/// <see cref="ResetTo"/>が履歴ごと捨てていたため、保存後のCtrl+Zがテキスト履歴への委譲になり、
/// 表示の追従がRoslyn再解析待ちで目に見えて遅かった。
/// </para>
/// </summary>
internal sealed class SceneEditDocument {
    private readonly SceneKey Key;
    private readonly SceneProjectionReconciler Reconciler;
    private readonly List<SceneUndoEntry>UndoStack=[];
    private readonly List<SceneUndoEntry>RedoStack=[];
    private SceneEditSnapshot Current;

    internal SceneEditDocument(SceneKey Key,SceneEditSnapshot Initial,SceneProjectionReconciler Reconciler){
        this.Key=Key;
        this.Reconciler=Reconciler??throw new ArgumentNullException(nameof(Reconciler));
        this.Current=Initial??throw new ArgumentNullException(nameof(Initial));
    }

    internal SceneModel Model=>this.Current.Model;

    internal SceneProjection Projection=>this.Current.Projection;

    /// <summary>
    /// M0051 Step 1-6: <see cref="UndoStack"/>のうち、既にC#テキストへ書き出した件数。
    ///
    /// <para>
    /// <b>不変条件</b>: <c>0 ≤ TextWatermark ≤ UndoStack.Count</c> かつ
    /// 「C#テキスト == History[0..TextWatermark) を再生した状態」。
    /// 保存はこの位置から先だけを書き出し、テキストごと戻すUndoはこれを1つ戻す
    /// (<see cref="SceneTextWatermarkPolicy"/>)。
    /// </para>
    /// </summary>
    internal int TextWatermark { get;private set;}

    /// <summary>
    /// M0051 Step 1-6: 参照比較(<c>LastSavedModel</c>)からウォーターマーク比較へ置き換えた。
    ///
    /// <para>
    /// 履歴をセッション全体で保持するようになったため、参照比較のままでは<b>保存が壊れる</b>:
    /// 保存済みの編集をUndoで戻すとModelの参照は変わるので<c>IsDirty</c>は真になるが、
    /// 書き出すべき部分列は空なので<see cref="SceneSourceEditor.ApplyForSaveSteps"/>が
    /// <c>FinalRoot=null</c>を返して「保存に失敗しました」になる。
    /// ウォーターマーク比較なら「ちょうど書き出し済みの位置に居るときはdirtyでない」が自然に成り立つ
    /// (M0047が参照比較で表現していた設計意図そのもの)。
    /// </para>
    /// </summary>
    internal bool IsDirty=>this.UndoStack.Count!=this.TextWatermark;

    internal bool CanUndo=>this.UndoStack.Count>0;

    internal bool CanRedo=>this.RedoStack.Count>0;

    /// <summary>Redoで進める件数。<see cref="SceneTextWatermarkPolicy.DecideRedo"/>が使う。</summary>
    internal int RedoCount=>this.RedoStack.Count;

    /// <summary>直近のUndo操作ラベル。VSツールバーの履歴表示に使う想定。</summary>
    internal string?LastUndoLabel=>this.UndoStack.Count>0?this.UndoStack[^1].Label:null;

    internal string?LastRedoLabel=>this.RedoStack.Count>0?this.RedoStack[^1].Label:null;

    internal bool TryApply(IReadOnlyList<SceneEditOperation>Operations,string Label){
        if(!SceneModelEditApplier.TryApply(
            this.Current.Model,this.Current.Projection,this.Reconciler,this.Key,Operations,
            out SceneModel Predicted,out SceneProjection PredictedProjection)){
            return false;
        }

        var After=new SceneEditSnapshot(Predicted,PredictedProjection);
        this.UndoStack.Add(new SceneUndoEntry(this.Current,After,Operations,Label));
        this.Current=After;
        this.RedoStack.Clear();
        return true;
    }

    internal bool TryUndo(){
        if(this.UndoStack.Count==0){
            return false;
        }

        SceneUndoEntry Entry=this.UndoStack[^1];
        this.UndoStack.RemoveAt(this.UndoStack.Count-1);
        this.RedoStack.Add(Entry);
        this.Current=Entry.Before;
        return true;
    }

    internal bool TryRedo(){
        if(this.RedoStack.Count==0){
            return false;
        }

        SceneUndoEntry Entry=this.RedoStack[^1];
        this.RedoStack.RemoveAt(this.RedoStack.Count-1);
        this.UndoStack.Add(Entry);
        this.Current=Entry.After;
        return true;
    }

    /// <summary>
    /// 初回の編集からCurrentへ至る、実際に適用されたEntryの全列(Undoで戻された分は含まない)。
    /// </summary>
    internal IReadOnlyList<SceneUndoEntry>History=>this.UndoStack;

    /// <summary>
    /// M0051 Step 1-6: まだC#テキストへ書き出していない履歴。保存はこれだけを再生する
    /// (<see cref="SceneSourceEditor.ApplyForSaveSteps"/>)。以前は<see cref="History"/>全体を毎回
    /// 先頭から再生していたが、履歴をセッション全体で保持するようになったのでそれでは
    /// 二重適用になる。
    /// </summary>
    internal IReadOnlyList<SceneUndoEntry>UnwrittenHistory=>
        this.UndoStack.Count>this.TextWatermark
            ?this.UndoStack.GetRange(this.TextWatermark,this.UndoStack.Count-this.TextWatermark)
            :[];

    /// <summary>保存が<see cref="UnwrittenHistory"/>を書き出し切ったときに呼ぶ。</summary>
    internal void MarkWritten()=>this.TextWatermark=this.UndoStack.Count;

    /// <summary>
    /// テキストごと1件戻したときに呼ぶ(<see cref="SceneUndoAction.InMemoryWithText"/>)。
    /// <see cref="TryUndo"/>が<see cref="UndoStack"/>を1つ減らすのと対で動き、不変条件を保つ。
    /// </summary>
    internal void RollbackWatermark(){
        if(this.TextWatermark>0){
            this.TextWatermark--;
        }
    }

    /// <summary>テキストごと1件進めたときに呼ぶ(Redo側)。</summary>
    internal void AdvanceWatermark(){
        if(this.TextWatermark<this.UndoStack.Count){
            this.TextWatermark++;
        }
    }

    /// <summary>
    /// 外部でSceneが再解析されたとき(他経路でC#が変わった、またはDocumentと実解析のリネージが
    /// 食い違った)、このDocumentを新しい実解析結果へ丸ごと差し替える。未保存の編集は失われる —
    /// 呼び出し側が(外部編集検知時の確認ダイアログ等で)適切なタイミングでだけ呼ぶこと。
    /// ウォーターマークも0へ戻す(新しい実解析結果が新たな原点になる)。
    /// </summary>
    internal void ResetTo(SceneEditSnapshot Snapshot){
        this.Current=Snapshot;
        this.UndoStack.Clear();
        this.RedoStack.Clear();
        this.TextWatermark=0;
    }
}
