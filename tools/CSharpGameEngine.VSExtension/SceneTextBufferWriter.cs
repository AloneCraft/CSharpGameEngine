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
    /// 血縁を作れない書き込み(シーン JSON の書き戻し、<c>ComponentIconGlyphMargin</c>)は
    /// <see cref="PlanMinimal"/>を使う(2026-10-04 から。それまではこの全文置換だった)。
    /// </summary>
    internal static IReadOnlyList<TextChange>Plan(SourceText Before,SourceText After)=>
        After.GetTextChanges(Before)
            .OrderByDescending(static Change=>Change.Span.Start)
            .ToArray();

    /// <summary>
    /// The one change between two unrelated texts that leaves their common start and end alone, for a scene JSON
    /// write-back: its text merge (M0095) already changes only the edited values, but <see cref="Plan(SourceText,SourceText)"/>
    /// replaced the whole document on every edit. A boundary never splits a CRLF or a surrogate pair.
    /// </summary>
    internal static IReadOnlyList<TextChange>PlanMinimal(SourceText Before,string After){
        var Old=Before.ToString();
        var Limit=Math.Min(Old.Length,After.Length);
        var Prefix=0;
        while(Prefix<Limit&&Old[Prefix]==After[Prefix]){ Prefix++; }
        if(Prefix==Old.Length&&Prefix==After.Length){ return []; }
        var Suffix=0;
        while(Suffix<Limit-Prefix&&Old[Old.Length-1-Suffix]==After[After.Length-1-Suffix]){ Suffix++; }
        static bool Splits(string Text,int At)=>At>0&&At<Text.Length
            &&(Text[At-1]=='\r'&&Text[At]=='\n'||char.IsHighSurrogate(Text[At-1])&&char.IsLowSurrogate(Text[At]));
        while(Prefix>0&&(Splits(Old,Prefix)||Splits(After,Prefix))){ Prefix--; }
        while(Suffix>0&&(Splits(Old,Old.Length-Suffix)||Splits(After,After.Length-Suffix))){ Suffix--; }
        return [new TextChange(new TextSpan(Prefix,Old.Length-Prefix-Suffix),After.Substring(Prefix,After.Length-Prefix-Suffix))];
    }

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
        this.TryApply(Buffer,Before,SceneTextChangePlanner.PlanMinimal(Before,After.ToString()),UndoDescription);

    /// <summary>
    /// 変更を開始位置の降順(<see cref="SceneTextChangePlanner"/>が整列済み)で適用する。
    ///
    /// <para>
    /// M0044で一度<see cref="ITextEdit"/>による一括適用へ変えたが、実機でギズモ編集が
    /// <b>成功扱いのまま何も書き込まれなくなった</b>ため元の<c>Buffer.Replace</c>へ戻した。
    /// ツリー差分側(<see cref="SceneTextChangePlanner.Plan(SyntaxTree,SyntaxTree)"/>)は本番と同じ
    /// 経路(注釈付きApplyBatch・同型兄弟20件)をユニットテストで再現して健全性を確認できているが、
    /// この適用部はVS SDK依存でテストできず、実績のある形を保つ方を優先する。差分が最小化された
    /// 結果、通常の編集では変更が1個なので一括適用の利点はもともとほとんど無い。
    /// </para>
    /// </summary>
    internal bool TryApply(
        ITextBuffer Buffer,SourceText Before,IReadOnlyList<TextChange>Changes,string UndoDescription){
        if(!SceneTextBufferApplyGuard.Matches(Before,Buffer.CurrentSnapshot.GetText())){
            return false;
        }
        if(Changes.Count==0){
            return true;
        }

        var History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        using(var Transaction=History.CreateTransaction(UndoDescription)){
            foreach(var Change in Changes){
                Buffer.Replace(
                    new Microsoft.VisualStudio.Text.Span(Change.Span.Start,Change.Span.Length),
                    Change.NewText);
            }

            Transaction.Complete();
        }

        return true;
    }

    // M0036: シーンビューのCtrl+Z/Yから呼ばれる。Undo/Redoの唯一の実体は引き続きVSの
    // ITextUndoHistoryであり(ADR-0040、SceneModel自体は編集履歴を持たない)、ここは対象バッファの
    // 履歴を直接叩くだけの薄いラッパー。Sessionは成功時だけRoslyn再解析をscheduleする。
    internal bool TryUndo(ITextBuffer Buffer){
        var History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        if(!History.CanUndo){
            return false;
        }

        History.Undo(1);
        return true;
    }

    internal bool TryRedo(ITextBuffer Buffer){
        var History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        if(!History.CanRedo){
            return false;
        }

        History.Redo(1);
        return true;
    }

}
