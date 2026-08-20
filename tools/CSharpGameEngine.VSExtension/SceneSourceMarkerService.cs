using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using VsTextBuffer=Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Applies source markers through Visual Studio's built-in text-marker tagger. It intentionally
/// never opens or activates a document while updating: Hierarchy must remain usable for
/// consecutive selection changes.
/// </summary>
internal sealed class SceneSourceMarkerService : IDisposable {
    private readonly IVsRunningDocumentTable RunningDocumentTable;
    private readonly IVsEditorAdaptersFactoryService EditorAdapters;
    private readonly IVsTextManager TextManager;
    private readonly ITextMarkerProviderFactory TextMarkerProviderFactory;
    private ITextBuffer? MarkedBuffer;
    private SimpleTagger<TextMarkerTag>?MarkerTagger;
    private List<TrackingTagSpan<TextMarkerTag>>OwnedTagSpans=[];
    private bool Disposed;

    private SceneSourceMarkerService(
        IVsRunningDocumentTable RunningDocumentTable,IVsEditorAdaptersFactoryService EditorAdapters,
        IVsTextManager TextManager,ITextMarkerProviderFactory TextMarkerProviderFactory){
        this.RunningDocumentTable=RunningDocumentTable;
        this.EditorAdapters=EditorAdapters;
        this.TextManager=TextManager;
        this.TextMarkerProviderFactory=TextMarkerProviderFactory;
    }

    internal static SceneSourceMarkerService?Start(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Package.GetGlobalService(typeof(SVsRunningDocumentTable))is not IVsRunningDocumentTable RunningDocumentTable
            ||Package.GetGlobalService(typeof(SVsTextManager))is not IVsTextManager TextManager
            ||Package.GetGlobalService(typeof(SComponentModel))is not IComponentModel ComponentModel){
            LogWarning("Visual Studio services required for hierarchy source markers are unavailable.");
            return null;
        }

        var EditorAdapters=ComponentModel.GetService<IVsEditorAdaptersFactoryService>();
        var TextMarkerProviderFactory=ComponentModel.GetService<ITextMarkerProviderFactory>();
        if(EditorAdapters is null||TextMarkerProviderFactory is null){
            LogWarning("Visual Studio text marker service is unavailable; hierarchy source markers are disabled.");
            return null;
        }

        LogInformation("Visual Studio text marker service acquired.");
        return new SceneSourceMarkerService(
            RunningDocumentTable,EditorAdapters,TextManager,TextMarkerProviderFactory);
    }

    /// <summary>
    /// M0043: スクロールするかどうかは<paramref name="ScrollBehavior"/>で<b>呼び出し側が明示する</b>。
    /// 以前はここで「選択が変わったか」を推測していたが、その判定は選択の変化とは無関係な条件
    /// (<c>Clear()</c>後の<c>MarkedBuffer==null</c>)でもtrueになり、編集のたびに無関係な位置へ
    /// 飛んでいた(<see cref="SceneSourceMarkerScrollPolicy"/>参照)。
    /// </summary>
    internal void Update(
        SceneModel? Model,IReadOnlyList<NodePath> Paths,SceneSourceMarkerScrollBehavior ScrollBehavior){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){
            return;
        }

        if(Model?.Root is null||Model.HasErrors){
            LogInformation("Cleared hierarchy source markers because the scene model is unavailable or invalid.");
            this.Clear();
            return;
        }

        if(!this.TryGetOpenBuffer(Model.FilePath,out var FoundBuffer,out var NativeBuffer)
            ||FoundBuffer is null){
            LogInformation($"Skipped hierarchy source markers because '{Model.FilePath}' is not an open text document.");
            this.Clear();
            return;
        }

        var Buffer=FoundBuffer;
        // バッファが差し替わったら、前のバッファへ貼ったタグは追跡できないので作り直す
        // (タグ再構築のための判定であって、スクロールするかどうかとは無関係)。
        if(!ReferenceEquals(this.MarkedBuffer,Buffer)){
            this.Clear();
            this.MarkedBuffer=Buffer;
        }

        var Ranges=SceneSourceMarkerPlanner.Create(Model,Paths);
        this.ReplaceOwnedTagSpans(Buffer,Ranges);

        if(SceneSourceMarkerScrollPolicy.ShouldScroll(ScrollBehavior,Ranges.Count)&&NativeBuffer is not null){
            this.ScrollToTop(NativeBuffer,Ranges[0].Span.Start);
        }
    }

    /// <summary>
    /// マーク範囲の先頭行をエディタ最上部へ、フォーカスを奪わずスクロールする。<c>IVsTextManager.
    /// EnumViews</c>は既存ビューの列挙だけで済み、対象文書をアクティブ化しない(<see cref="Navigate"/>の
    /// <c>GetActiveView</c>+<c>SetCaretPos</c>とは異なりcaretも動かさない)。
    /// </summary>
    private void ScrollToTop(VsTextBuffer NativeBuffer,int Position){
        if(this.TextManager.EnumViews(NativeBuffer,out var ViewEnum)!=VSConstants.S_OK
            ||ViewEnum is null){
            return;
        }

        var Fetched=new IVsTextView[1];
        uint FetchedCount=0;
        while(ViewEnum.Next(1,Fetched,ref FetchedCount)==VSConstants.S_OK&&FetchedCount==1){
            if(this.EditorAdapters.GetWpfTextView(Fetched[0])is not { }WpfView){
                continue;
            }

            var Snapshot=WpfView.TextBuffer.CurrentSnapshot;
            if(Position<0||Position>Snapshot.Length){
                continue;
            }

            WpfView.DisplayTextLineContainingBufferPosition(
                new SnapshotPoint(Snapshot,Position),0.0,ViewRelativePosition.Top);
        }
    }

    internal void Navigate(SceneModel? Model,NodePath Path){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||Model is null||!this.TryGetOpenBuffer(Model.FilePath,out var FoundBuffer,out _)
            ||FoundBuffer is null
            ||!SceneSourceMarkerPlanner.TryCreateNavigation(Model,Path,IsDocumentOpen:true,out var FilePath,out var Position)
            ||Package.GetGlobalService(typeof(SDTE))is not DTE Dte){
            LogInformation("Skipped hierarchy source navigation because the target document is not already open.");
            return;
        }

        Dte.ItemOperations.OpenFile(FilePath);
        if(this.TextManager.GetActiveView(1,null,out var View)!=VSConstants.S_OK){
            return;
        }

        var Snapshot=FoundBuffer.CurrentSnapshot;
        if(Position<0||Position>Snapshot.Length){
            return;
        }

        var Line=Snapshot.GetLineFromPosition(Position);
        var Column=Position-Line.Start.Position;
        if(ErrorHandler.Succeeded(View.SetCaretPos(Line.LineNumber,Column))){
            View.EnsureSpanVisible(new Microsoft.VisualStudio.TextManager.Interop.TextSpan {
                iStartLine=Line.LineNumber,
                iStartIndex=Column,
                iEndLine=Line.LineNumber,
                iEndIndex=Column,
            });
            LogInformation($"Moved the caret to hierarchy source position {Position} in '{FilePath}'.");
        }
    }

    private bool TryGetOpenBuffer(string FilePath,out ITextBuffer?Buffer,out VsTextBuffer?NativeBuffer){
        ThreadHelper.ThrowIfNotOnUIThread();
        Buffer=null;
        NativeBuffer=null;
        if(String.IsNullOrWhiteSpace(FilePath)){
            return false;
        }

        string CanonicalFilePath;
        try {
            CanonicalFilePath=Path.GetFullPath(FilePath);
        } catch(Exception Ex)when(Ex is ArgumentException||Ex is NotSupportedException||Ex is PathTooLongException){
            LogWarning($"Could not normalize hierarchy source path '{FilePath}': {Ex.Message}");
            return false;
        }

        var DocDataPointer=IntPtr.Zero;
        try {
            var HResult=this.RunningDocumentTable.FindAndLockDocument(
                                                                      (uint)_VSRDTFLAGS.RDT_NoLock,CanonicalFilePath,out _,out _,out DocDataPointer,out _);
            if(ErrorHandler.Failed(HResult)||DocDataPointer==IntPtr.Zero){
                return false;
            }

            var DocData=Marshal.GetObjectForIUnknown(DocDataPointer);
            var VsBuffer=DocData as VsTextBuffer;
            if(VsBuffer is null&&DocData is IVsTextBufferProvider Provider
                &&ErrorHandler.Succeeded(Provider.GetTextBuffer(out var Lines))){
                VsBuffer=Lines as VsTextBuffer;
            }

            NativeBuffer=VsBuffer;
            Buffer=VsBuffer is null?null:this.EditorAdapters.GetDocumentBuffer(VsBuffer);
            return Buffer is not null;
        } catch(COMException Ex){
            LogWarning($"Could not obtain the open text buffer for '{CanonicalFilePath}': {Ex.Message}");
            return false;
        } finally {
            if(DocDataPointer!=IntPtr.Zero){
                Marshal.Release(DocDataPointer);
            }
        }
    }

    private void Clear(){
        var Tagger=this.MarkerTagger;
        var OldTagSpans=this.OwnedTagSpans;
        this.MarkedBuffer=null;
        this.MarkerTagger=null;
        this.OwnedTagSpans=[];
        if(Tagger is null||OldTagSpans.Count==0){
            return;
        }

        try {
            using(Tagger.Update()){
                foreach(var TagSpan in OldTagSpans){
                    Tagger.RemoveTagSpan(TagSpan);
                }
            }

            LogInformation($"Removed {OldTagSpans.Count} hierarchy source marker tag(s).");
        } catch(Exception Ex){
            // A document can be disposed by VS between RDT lookup and this call. Markers are
            // best-effort decoration and must never destabilize the package during shutdown.
            LogWarning($"Could not clear hierarchy source marker tags: {Ex.Message}");
        }
    }

    private void ReplaceOwnedTagSpans(ITextBuffer Buffer,IReadOnlyList<SceneSourceMarkerRange>Ranges){
        var Snapshot=Buffer.CurrentSnapshot;
        List<ITrackingSpan>TrackingSpans=[];
        foreach(var Range in Ranges){
            var Span=Range.Span;
            if(Span.Start<0||Span.End>Snapshot.Length){
                LogWarning($"Skipped an out-of-date hierarchy source marker range ({Span.Start}, {Span.Length}).");
                continue;
            }

            TrackingSpans.Add(Snapshot.CreateTrackingSpan(
                Span.Start,Span.Length,SpanTrackingMode.EdgeExclusive));
        }

        SimpleTagger<TextMarkerTag>?Tagger=null;
        List<TrackingTagSpan<TextMarkerTag>>NewTagSpans=[];
        try {
            Tagger=this.TextMarkerProviderFactory.GetTextMarkerTagger(Buffer);
            if(Tagger is null){
                LogWarning("Visual Studio returned no standard text marker tagger for the open source buffer.");
                this.Clear();
                return;
            }

            using(Tagger.Update()){
                foreach(var OldTagSpan in this.OwnedTagSpans){
                    Tagger.RemoveTagSpan(OldTagSpan);
                }

                foreach(var TrackingSpan in TrackingSpans){
                    NewTagSpans.Add(Tagger.CreateTagSpan(TrackingSpan,SceneSourceMarkerFormatDefinition.MarkerTag));
                }
            }

            this.MarkerTagger=Tagger;
            this.OwnedTagSpans=NewTagSpans;
            LogInformation($"Applied {NewTagSpans.Count} hierarchy source marker tag(s) to the already-open buffer.");
        } catch(Exception Ex){
            // Do not leave tags from a partial update behind. This can occur if VS disposes the
            // text buffer while a scene re-analysis notification is being processed.
            if(Tagger is not null&&NewTagSpans.Count>0){
                try {
                    using(Tagger.Update()){
                        foreach(var TagSpan in NewTagSpans){
                            Tagger.RemoveTagSpan(TagSpan);
                        }
                    }
                } catch(Exception CleanupEx){
                    LogWarning($"Could not roll back partial hierarchy source marker tags: {CleanupEx.Message}");
                }
            }

            this.MarkerTagger=null;
            this.OwnedTagSpans=[];
            LogWarning($"Could not apply hierarchy source marker tags: {Ex.Message}");
        }
    }

    private static void LogInformation(string Message){
        try {
            ActivityLog.LogInformation(nameof(SceneSourceMarkerService),Message);
        } catch {
            // ActivityLog itself is unavailable during some shell teardown paths.
        }
    }

    private static void LogWarning(string Message){
        try {
            ActivityLog.LogWarning(nameof(SceneSourceMarkerService),Message);
        } catch {
            // ActivityLog itself is unavailable during some shell teardown paths.
        }
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.Clear();
    }
}

[Export(typeof(EditorFormatDefinition))]
[Name(SceneSourceMarkerFormatDefinition.MarkerTypeName)]
[UserVisible(true)]
internal sealed class SceneSourceMarkerFormatDefinition : MarkerFormatDefinition {
    internal const string MarkerTypeName="MarkerFormatDefinition/CSharpGameEngine.SceneHierarchySource";
    internal static readonly TextMarkerTag MarkerTag=new(MarkerTypeName);

    // Visual Studio instantiates exported format definitions through MEF. Unlike the provider,
    // this constructor must be public; otherwise TextMarkerTag has no display format.
    public SceneSourceMarkerFormatDefinition(){
        this.DisplayName="C# Game Engine Scene Hierarchy Source";
        this.BackgroundColor=System.Windows.Media.Color.FromArgb(0x50,0x6A,0x3D,0xC9);
        this.ZOrder=5;
    }
}
