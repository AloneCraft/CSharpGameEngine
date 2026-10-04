using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Points <see cref="SceneWorkspaceService"/> at whichever document the user is looking at
/// (ADR-0040). Hierarchy follows the scene of the active scene JSON (<c>Scenes\**\*.json</c>, ADR-0070), so it has to
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
    private readonly IVsUIShell UIShell;
    private readonly SceneWorkspaceService Service;
    private uint Cookie;
    private bool Disposed;

    private ActiveDocumentTracker(
        IVsMonitorSelection Selection,IVsTextManager TextManager,IVsUIShell UIShell,SceneWorkspaceService Service){
        this.Selection=Selection;
        this.TextManager=TextManager;
        this.UIShell=UIShell;
        this.Service=Service;
    }

    internal static ActiveDocumentTracker?Start(SceneWorkspaceService Service){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Package.GetGlobalService(typeof(SVsShellMonitorSelection))is not IVsMonitorSelection Selection
            ||Package.GetGlobalService(typeof(SVsTextManager))is not IVsTextManager TextManager
            ||Package.GetGlobalService(typeof(SVsUIShell))is not IVsUIShell UIShell){
            return null;
        }

        ActiveDocumentTracker Tracker=new(Selection,TextManager,UIShell,Service);
        Selection.AdviseSelectionEvents(Tracker,out Tracker.Cookie);

        // Pick up the document that is already open, so opening the tool window on an existing
        // editor does not require touching the file first.
        Tracker.RefreshCurrentDocument();

        return Tracker;
    }

    /// <summary>
    /// Re-reads the current frame/view after package startup. Visual Studio can expose the live
    /// workspace before its first text view is readable, so the one synchronous read in
    /// <see cref="Start"/> is not sufficient on a cold hive.
    /// </summary>
    internal bool RefreshCurrentDocument(){
        ThreadHelper.ThrowIfNotOnUIThread();

        IVsWindowFrame?Frame=null;
        if(this.Selection.GetCurrentElementValue(
            (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame,
            out var Current)==VSConstants.S_OK){
            Frame=Current as IVsWindowFrame;
        }
        this.UpdateFromFrame(Frame);
        return this.CurrentFilePath is not null;
    }

    public int OnElementValueChanged(uint ElementId,object OldValue,object NewValue){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(ElementId==(uint)VSConstants.VSSELELEMID.SEID_DocumentFrame){
            this.UpdateFromFrame(NewValue as IVsWindowFrame);
        }

        return VSConstants.S_OK;
    }

    /// <summary>Solution Explorer selection does not target a scene (ADR-0071: scenes open by double-click).</summary>
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
        var FramePath=TryGetFrameDocumentPath(Frame,out var DocumentPath)?DocumentPath:null;
        var TextViewPath=this.TryGetActiveTextViewPath(out DocumentPath)?DocumentPath:null;
        var SelectedPath=ActiveDocumentPathSelector.Select(FramePath,TextViewPath, this.CurrentFilePath);
        if(SelectedPath is not null){
            this.SetDocumentPath(SelectedPath);
        }
        // If neither source has a document, this is an unknown tool-window transition. Preserve the
        // current code document; clearing here was the Phase 8 bug that blanked Scene Designer.
    }

    private void SetDocumentPath(string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        this.CurrentFilePath=SceneJsonCommitPlanner.IsJsonScene(DocumentPath)
                                 ?DocumentPath
                                 :null;
        this.Service.SetActiveDocument(this.CurrentFilePath);
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

    private bool TryGetActiveTextView(out IVsTextView View,out string DocumentPath){
        ThreadHelper.ThrowIfNotOnUIThread();

        View=null!;
        DocumentPath=string.Empty;
        if(this.TextManager.GetActiveView(0,null,out View)!=VSConstants.S_OK
            ||View.GetBuffer(out var Buffer)!=VSConstants.S_OK
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
