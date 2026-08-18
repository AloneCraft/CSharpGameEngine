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
/// C#ソースへは一切書き込まない。書き込みは呼び出し側が保存操作のタイミングで
/// <see cref="AllOperationsInOrder"/>を<see cref="SceneSourceEditor.ApplyBatch"/>へまとめて渡し、
/// 得られた構文木を1回のVS Undoトランザクションとして書き戻す(呼び出し側の責務)。
/// </para>
///
/// <para>
/// <see cref="IsDirty"/>は保存時点の<see cref="SceneModel"/>インスタンスとの参照比較で決める。
/// <see cref="SceneModel"/>はimmutableなので、Undo/Redoでちょうど保存時点まで戻れば自動的に
/// dirtyでなくなる(値の再比較を一切書かなくていい)。
/// </para>
/// </summary>
internal sealed class SceneEditDocument {
    private readonly SceneKey Key;
    private readonly SceneProjectionReconciler Reconciler;
    private readonly List<SceneUndoEntry>UndoStack=[];
    private readonly List<SceneUndoEntry>RedoStack=[];
    private SceneEditSnapshot Current;
    private SceneModel LastSavedModel;

    internal SceneEditDocument(SceneKey Key,SceneEditSnapshot Initial,SceneProjectionReconciler Reconciler){
        this.Key=Key;
        this.Reconciler=Reconciler??throw new ArgumentNullException(nameof(Reconciler));
        this.Current=Initial??throw new ArgumentNullException(nameof(Initial));
        this.LastSavedModel=Initial.Model;
    }

    internal SceneModel Model=>this.Current.Model;

    internal SceneProjection Projection=>this.Current.Projection;

    internal bool IsDirty=>!ReferenceEquals(this.Current.Model,this.LastSavedModel);

    internal bool CanUndo=>this.UndoStack.Count>0;

    internal bool CanRedo=>this.RedoStack.Count>0;

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
    /// 保存時の書き戻し(<see cref="SceneSourceEditor.ApplyForSave"/>)に使う。
    /// </summary>
    internal IReadOnlyList<SceneUndoEntry>History=>this.UndoStack;

    internal void MarkSaved()=>this.LastSavedModel=this.Current.Model;

    /// <summary>
    /// 外部でSceneが再解析されたとき(自分の保存が着地した、または他経路でC#が変わった)、
    /// このDocumentを新しい実解析結果へ丸ごと差し替える。未保存の編集は失われる — 呼び出し側が
    /// (外部編集検知時の確認ダイアログ等で)適切なタイミングでだけ呼ぶこと。
    /// </summary>
    internal void ResetTo(SceneEditSnapshot Snapshot){
        this.Current=Snapshot;
        this.LastSavedModel=Snapshot.Model;
        this.UndoStack.Clear();
        this.RedoStack.Clear();
    }
}
