using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;

namespace CSharpGameEngine.VSExtension;

/// <summary>SourceText差分をlive TextBufferへ適用できる順序(開始位置の降順)に整列する計画層。</summary>
internal static class SceneTextChangePlanner {
    /// <summary>
    /// M0044: <b>この経路は差分が粗い。</b><see cref="SourceText.GetTextChanges"/>は、
    /// <paramref name="After"/>が<paramref name="Before"/>と血縁の無い(<c>WithChanges</c>由来でない)
    /// テキストのとき、実測で<b>ファイル全体を覆う1個の変更</b>を返す(18KBのファイルで1文字だけ
    /// 変えても span=(0,18531))。可能なら<see cref="Plan(SyntaxTree,SyntaxTree)"/>を使うこと。
    /// こちらは血縁を作れない呼び出し元(<c>ComponentIconGlyphMargin</c>)専用に残している。
    /// </summary>
    internal static IReadOnlyList<TextChange>Plan(SourceText Before,SourceText After)=>
        After.GetTextChanges(Before)
            .OrderByDescending(static Change=>Change.Span.Start)
            .ToArray();

    /// <summary>
    /// M0044: 構文ツリー同士の差分。<paramref name="After"/>が<paramref name="Before"/>のrootへ
    /// <c>ReplaceNode</c>して得たツリーなら、green nodeの共有構造から<b>実際に変わった領域だけ</b>を
    /// 返す(実測: 単一プロパティ編集で10文字、離れた2箇所の編集で2個・計11文字)。
    ///
    /// <para>
    /// 書き換え量が小さいほどエディタの再レイアウトも局所化する。<see cref="Plan(SourceText,SourceText)"/>の
    /// 全文置換はビューのレイアウト基準点ごと作り直させるため、編集のたびに表示が一度最上部へ
    /// 飛んでから戻る、というちらつきの原因になっていた。
    /// </para>
    /// </summary>
    internal static IReadOnlyList<TextChange>Plan(SyntaxTree Before,SyntaxTree After)=>
        After.GetChanges(Before)
            .OrderByDescending(static Change=>Change.Span.Start)
            .ToArray();
}

internal static class SceneTextBufferApplyGuard {
    internal static bool Matches(SourceText Before,string LiveText)=>
        string.Equals(Before.ToString(),LiveText,StringComparison.Ordinal);
}

/// <summary>
/// Designerが編集したRoslyn結果をVisual Studioのlive TextBufferへ反映し、Undo単位を成立させる。
/// 1ユーザー操作を1つのITextUndoTransactionで括る。TextBufferへの適用はI/OやRoslyn解析を伴わない
/// UI thread上の同期処理であり、bridge processや別Taskへは逃がさない。
/// </summary>
internal sealed class SceneTextBufferWriter {
    private readonly ITextUndoHistoryRegistry UndoHistoryRegistry;

    internal SceneTextBufferWriter(ITextUndoHistoryRegistry UndoHistoryRegistry)=>
        this.UndoHistoryRegistry=UndoHistoryRegistry??throw new ArgumentNullException(nameof(UndoHistoryRegistry));

    internal bool TryApply(ITextBuffer Buffer,SourceText Before,SourceText After,string UndoDescription)=>
        this.TryApply(Buffer,Before,SceneTextChangePlanner.Plan(Before,After),UndoDescription);

    /// <summary>
    /// M0044: 全変更を1つの<see cref="ITextEdit"/>へまとめて<see cref="ITextEdit.Apply"/>する。
    /// 以前は変更ごとに<c>Buffer.Replace</c>を個別に呼んでおり、1回ごとに独立したバッファ変更・
    /// <c>TextChanged</c>イベント・ビューのレイアウトが走っていた。VSには描画を止める公開APIが
    /// 無く、まとめて反映する手段がこの<see cref="ITextEdit"/>のバッチ適用にあたる。
    /// spanは全て編集前スナップショットの座標で解釈されるため降順整列は不要になるが、
    /// <see cref="SceneTextChangePlanner"/>の整列は無害なのでそのまま受け取る。
    /// </summary>
    internal bool TryApply(
        ITextBuffer Buffer,SourceText Before,IReadOnlyList<TextChange>Changes,string UndoDescription){
        if(!SceneTextBufferApplyGuard.Matches(Before,Buffer.CurrentSnapshot.GetText())){
            return false;
        }
        if(Changes.Count==0){
            return true;
        }

        ITextUndoHistory History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        using(ITextUndoTransaction Transaction=History.CreateTransaction(UndoDescription)){
            using(ITextEdit Edit=Buffer.CreateEdit()){
                foreach(TextChange Change in Changes){
                    if(!Edit.Replace(
                        new Microsoft.VisualStudio.Text.Span(Change.Span.Start,Change.Span.Length),
                        Change.NewText)){
                        // 以前のループは適用失敗を一切検知していなかった。ここで抜ければ
                        // using が Edit を破棄し、部分適用のまま抜けることはない。
                        return false;
                    }
                }

                Edit.Apply();
                if(Edit.Canceled){
                    return false;
                }
            }

            Transaction.Complete();
        }

        return true;
    }

    // M0036: シーンビューのCtrl+Z/Yから呼ばれる。Undo/Redoの唯一の実体は引き続きVSの
    // ITextUndoHistoryであり(ADR-0040、SceneModel自体は編集履歴を持たない)、ここは対象バッファの
    // 履歴を直接叩くだけの薄いラッパー。呼び出し元(SceneHwndHost)は成功時だけ表示の先読みパッチを
    // 行う。
    internal bool TryUndo(ITextBuffer Buffer){
        ITextUndoHistory History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        if(!History.CanUndo){
            return false;
        }

        History.Undo(1);
        return true;
    }

    internal bool TryRedo(ITextBuffer Buffer){
        ITextUndoHistory History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        if(!History.CanRedo){
            return false;
        }

        History.Redo(1);
        return true;
    }
}
