using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;

namespace CSharpGameEngine.VSExtension;

/// <summary>SourceText差分をlive TextBufferへ適用できる順序(開始位置の降順)に整列する計画層。</summary>
internal static class SceneTextChangePlanner {
    internal static IReadOnlyList<TextChange>Plan(SourceText Before,SourceText After)=>
        After.GetTextChanges(Before)
            .OrderByDescending(static Change=>Change.Span.Start)
            .ToArray();
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

    internal bool TryApply(ITextBuffer Buffer,SourceText Before,SourceText After,string UndoDescription){
        IReadOnlyList<TextChange>Changes=SceneTextChangePlanner.Plan(Before,After);
        if(Changes.Count==0){
            return true;
        }

        ITextUndoHistory History=this.UndoHistoryRegistry.RegisterHistory(Buffer);
        using(ITextUndoTransaction Transaction=History.CreateTransaction(UndoDescription)){
            foreach(TextChange Change in Changes){
                Buffer.Replace(
                    new Microsoft.VisualStudio.Text.Span(Change.Span.Start,Change.Span.Length),
                    Change.NewText);
            }

            Transaction.Complete();
        }

        return true;
    }
}
