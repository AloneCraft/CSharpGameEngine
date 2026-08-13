using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;

namespace CSharpGameEngine.VSExtension;

/// <summary>SourceText???live TextBuffer???????????????????????</summary>
internal static class SceneTextChangePlanner {
    internal static IReadOnlyList<TextChange>Plan(SourceText Before,SourceText After)=>
        After.GetTextChanges(Before)
            .OrderByDescending(static Change=>Change.Span.Start)
            .ToArray();
}

/// <summary>
/// Designer????Roslyn???Visual Studio?live TextBuffer?????????Undo???????
/// ?????????ITextUndoTransaction???????TextBuffer???I/O?Roslyn??????
/// ???????UI thread?bridge process?????Task??????????
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
