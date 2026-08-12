using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Points <see cref="SceneWorkspaceService"/> at whichever document the user is looking at
/// (ADR-0040). The Scene editor shows the scene declared by the active <c>.cs</c> file, so it has to
/// follow the document frame rather than the solution.
///
/// <para>
/// Uses <c>IVsMonitorSelection</c>'s document-frame element instead of DTE window events: the
/// element value is also readable synchronously at startup, which is what lets the Hierarchy
/// populate for a document that was already open when the package loaded.
/// </para>
/// </summary>
internal sealed class ActiveDocumentTracker : IVsSelectionEvents,IDisposable {
    private readonly IVsMonitorSelection Selection;
    private readonly SceneWorkspaceService Service;
    private uint Cookie;
    private bool Disposed;

    private ActiveDocumentTracker(IVsMonitorSelection Selection,SceneWorkspaceService Service){
        this.Selection=Selection;
        this.Service=Service;
    }

    internal static ActiveDocumentTracker?Start(SceneWorkspaceService Service){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Package.GetGlobalService(typeof(SVsShellMonitorSelection))is not IVsMonitorSelection Selection){
            return null;
        }

        ActiveDocumentTracker Tracker=new(Selection,Service);
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

        if(Frame is null){
            this.Service.SetActiveDocument(null);
            return;
        }

        if(Frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument,out var Value)!=VSConstants.S_OK
            ||Value is not string DocumentPath
            ||!DocumentPath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)){
            this.Service.SetActiveDocument(null);
            return;
        }

        this.Service.SetActiveDocument(this.Service.FindDocument(DocumentPath));
    }

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
