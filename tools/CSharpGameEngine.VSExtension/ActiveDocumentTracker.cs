using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Points <see cref="SceneWorkspaceService"/> at whichever document the user is looking at
/// (ADR-0040). The Scene editor shows the scene declared by the active <c>.cs</c> file, so it has to
/// follow the document frame rather than the solution.
///
/// <para>
/// Uses <c>IVsMonitorSelection</c>'s document-frame element instead of DTE window events, with
/// <c>IVsTextManager.GetActiveView(false)</c> as the fallback for transient tool-window focus.
/// Both are synchronously readable at startup, so Hierarchy can populate for an already-open file
/// without clearing it when focus moves to Scene Designer or Solution Explorer.
/// </para>
/// </summary>
internal sealed class ActiveDocumentTracker : IVsSelectionEvents,IDisposable {
    private readonly IVsMonitorSelection Selection;
    private readonly IVsTextManager TextManager;
    private readonly SceneWorkspaceService Service;
    private uint Cookie;
    private bool Disposed;

    private ActiveDocumentTracker(IVsMonitorSelection Selection,IVsTextManager TextManager,SceneWorkspaceService Service){
        this.Selection=Selection;
        this.TextManager=TextManager;
        this.Service=Service;
    }

    internal static ActiveDocumentTracker?Start(SceneWorkspaceService Service){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Package.GetGlobalService(typeof(SVsShellMonitorSelection))is not IVsMonitorSelection Selection
            ||Package.GetGlobalService(typeof(SVsTextManager))is not IVsTextManager TextManager){
            return null;
        }

        ActiveDocumentTracker Tracker=new(Selection,TextManager,Service);
        Selection.AdviseSelectionEvents(Tracker,out Tracker.Cookie);

        // Pick up the document that is already open, so opening the tool window on an existing
        // editor does not require touching the file first.
        if(Selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame,out var Current)==VSConstants.S_OK){
            Tracker.UpdateFromFrame(Current as IVsWindowFrame);
        }

        return Tracker;
    }

    public int OnElementValueChanged(uint ElementId,object OldValue,object NewValue){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(ElementId==(uint)VSConstants.VSSELELEMID.SEID_DocumentFrame){
            this.UpdateFromFrame(NewValue as IVsWindowFrame);
        }

        return VSConstants.S_OK;
    }

    public int OnSelectionChanged(
        IVsHierarchy OldHierarchy,
        uint OldItemId,
        IVsMultiItemSelect OldMultiItemSelect,
        ISelectionContainer OldSelectionContainer,
        IVsHierarchy NewHierarchy,
        uint NewItemId,
        IVsMultiItemSelect NewMultiItemSelect,
        ISelectionContainer NewSelectionContainer)=>VSConstants.S_OK;

    public int OnCmdUIContextChanged(uint CmdUICookie,int Active)=>VSConstants.S_OK;

    private void UpdateFromFrame(IVsWindowFrame?Frame){
        ThreadHelper.ThrowIfNotOnUIThread();

        // Tool-window activation can transiently publish a null/non-document frame. GetActiveView
        // with fMustHaveFocus=false returns the current or most recently active text view, so moving
        // focus to Scene Designer/Solution Explorer keeps the last code document instead of
        // clearing Hierarchy and racing OpenSceneDesignerCommand.
        string?FramePath=TryGetFrameDocumentPath(Frame,out var DocumentPath)?DocumentPath:null;
        string?TextViewPath=this.TryGetActiveTextViewPath(out DocumentPath)?DocumentPath:null;
        string?SelectedPath=ActiveDocumentPathSelector.Select(FramePath,TextViewPath,CurrentFilePath);
        if(SelectedPath is not null){
            this.SetDocumentPath(SelectedPath);
        }
        // If neither source has a document, this is an unknown tool-window transition. Preserve the
        // current code document; clearing here was the Phase 8 bug that blanked Scene Designer.
    }

    private void SetDocumentPath(string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        CurrentFilePath=DocumentPath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)
            ?DocumentPath
            :null;
        this.Service.SetActiveDocument(CurrentFilePath);
    }

    private static bool TryGetFrameDocumentPath(IVsWindowFrame?Frame,out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        DocumentPath=string.Empty;
        if(Frame is null
            ||Frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument,out var Value)!=VSConstants.S_OK
            ||Value is not string Path
            ||string.IsNullOrWhiteSpace(Path)){
            return false;
        }

        DocumentPath=Path;
        return true;
    }

    private bool TryGetActiveTextViewPath(out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        return this.TryGetActiveTextView(out _,out DocumentPath);
    }

    /// <summary>
    /// コマンド起動元のテキストビューを固定します。ToolWindow表示後にGetActiveViewを呼ぶと、
    /// 既にVSシェルへフォーカスが移った後なので、右クリック元を確実に復元できません。
    /// </summary>
    internal IVsTextView? CaptureActiveTextView(string ExpectedFilePath){
        ThreadHelper.ThrowIfNotOnUIThread();

        return this.TryGetActiveTextView(out IVsTextView View,out string DocumentPath)
            &&string.Equals(DocumentPath,ExpectedFilePath,StringComparison.OrdinalIgnoreCase)
            ?View
            :null;
    }

    /// <summary>
    /// 右クリックコマンドの型境界にはキャレット位置を使う。既にアクティブなテキストバッファだけを
    /// 同期的に読み、ここからRoslyn解析を開始してはならない。
    /// </summary>
    internal bool TryCaptureActiveCaretPosition(string ExpectedFilePath,out int Position){
        ThreadHelper.ThrowIfNotOnUIThread();

        Position=0;
        if(!this.TryGetActiveTextView(out IVsTextView View,out string DocumentPath)
            ||!string.Equals(DocumentPath,ExpectedFilePath,StringComparison.OrdinalIgnoreCase)
            ||View.GetCaretPos(out int Line,out int Column)!=VSConstants.S_OK
            ||View.GetBuffer(out IVsTextLines Buffer)!=VSConstants.S_OK){
            return false;
        }

        return Buffer.GetPositionOfLineIndex(Line,Column,out Position)==VSConstants.S_OK;
    }
    private bool TryGetActiveTextView(out IVsTextView View,out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        View=null!;
        DocumentPath=string.Empty;
        if(this.TextManager.GetActiveView(0,null,out View)!=VSConstants.S_OK
            ||View.GetBuffer(out IVsTextLines Buffer)!=VSConstants.S_OK
            ||Buffer is not IPersistFileFormat PersistFile
            ||PersistFile.GetCurFile(out var Path,out _)!=VSConstants.S_OK
            ||string.IsNullOrWhiteSpace(Path)){
            return false;
        }

        DocumentPath=Path;
        return true;
    }

    internal string?CurrentFilePath { get;private set;}

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;

        ThreadHelper.JoinableTaskFactory.Run(async ()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if(this.Cookie!=0){
                this.Selection.UnadviseSelectionEvents(this.Cookie);
                this.Cookie=0;
            }
        });
    }
}
