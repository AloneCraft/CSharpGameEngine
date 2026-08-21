using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

internal sealed class SceneDesignerDocumentManager:IDisposable {
    private const uint CreateNewWindow=0x00400000;
    private readonly Dictionary<SceneKey,SceneDesignerDocumentPane>Panes=[];
    private bool Disposed;

    internal void Open(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){ throw new ObjectDisposedException(nameof(SceneDesignerDocumentManager)); }
        if(this.Panes.TryGetValue(Snapshot.Key,out SceneDesignerDocumentPane? Existing)){
            Existing.SetTarget(Session,Snapshot);
            Existing.ShowWithoutActivation();
            return;
        }
        this.Create(Session,Snapshot);
    }

    private void Create(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.CreateWindow(Session,Snapshot);
    }
    private void CreateWindow(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Pane=new SceneDesignerDocumentPane(Snapshot.Key,this.OnPaneClosed);
        try {
            CreateFrame(Pane,Snapshot);
            Pane.SetTarget(Session,Snapshot);
            this.Panes.Add(Snapshot.Key,Pane);
            Pane.ShowWithoutActivation();
        } catch {
            Pane.Dispose();
            throw;
        }
    }

    private static void CreateFrame(SceneDesignerDocumentPane Pane,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Package.GetGlobalService(typeof(SVsUIShell))is not IVsUIShell Shell){
            throw new InvalidOperationException("Visual Studio's UI shell is unavailable.");
        }
        IntPtr View=IntPtr.Zero;
        try {
            View=Marshal.GetIUnknownForObject((IVsWindowPane)Pane);
            // This is an ad-hoc IVsWindowPane, not a registered editor factory. Passing a private
            // GUID makes the shell look up an editor that does not exist and prevents the document
            // frame from being created.
            Guid Editor=SceneDesignerDocumentWindowPolicy.EditorTypeGuid;
            Guid CommandUi=Guid.Empty;
            ErrorHandler.ThrowOnFailure(Shell.CreateDocumentWindow(
                grfCDW:CreateNewWindow,
                pszMkDocument:CreateMoniker(Snapshot.Key),
                pUIH:null,
                itemid:VSConstants.VSITEMID_NIL,
                punkDocView:View,
                punkDocData:IntPtr.Zero,
                rguidEditorType:ref Editor,
                pszPhysicalView:null,
                rguidCmdUI:ref CommandUi,
                psp:null,
                pszOwnerCaption:SceneDesignerDocumentPane.CreateCaption(Snapshot.Key),
                pszEditorCaption:null,
                pfDefaultPosition:null,
                ppWindowFrame:out IVsWindowFrame Frame));
            Pane.AttachFrame(Frame);
        } finally {
            if(View!=IntPtr.Zero){ Marshal.Release(View); }
        }
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        var OpenPanes=new List<SceneDesignerDocumentPane>(this.Panes.Values);
        foreach(SceneDesignerDocumentPane Pane in OpenPanes){ Pane.Dispose(); }
        this.Panes.Clear();
    }

    private void OnPaneClosed(SceneKey Key)=>this.Panes.Remove(Key);
    private static string CreateMoniker(SceneKey Key)=>
        "CSharpGameEngine.SceneDesigner://"+Uri.EscapeDataString(Key.ProjectId)+"/"+Uri.EscapeDataString(Key.ClassFullyQualifiedName);
}

internal sealed class SceneDesignerDocumentPane:WindowPane {
    private readonly SceneKey Key;
    private readonly Action<SceneKey>Closed;
    private readonly SceneDesignerToolWindowControl Control;
    private IVsWindowFrame?Frame;
    private bool IsClosed;

    internal SceneDesignerDocumentPane(SceneKey Key,Action<SceneKey>Closed):base(null){
        this.Key=Key;
        this.Closed=Closed??throw new ArgumentNullException(nameof(Closed));
        this.Control=new SceneDesignerToolWindowControl();
        this.Content=this.Control;
    }

    internal void AttachFrame(IVsWindowFrame Frame)=>this.Frame=Frame??throw new ArgumentNullException(nameof(Frame));
    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot)=>this.Control.SetTarget(Session,Snapshot);
    internal void ShowWithoutActivation(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Frame is not null){ ErrorHandler.ThrowOnFailure(this.Frame.ShowNoActivate()); }
    }

    internal static string CreateCaption(SceneKey Key){
        string Name=Key.ClassFullyQualifiedName;
        int LastSeparator=Name.LastIndexOf('.');
        return (LastSeparator>=0?Name.Substring(LastSeparator+1):Name)+" [Scene]";
    }

    protected override void OnClose(){
        if(!this.IsClosed){
            this.IsClosed=true;
            this.Closed(this.Key);
            this.Control.Dispose();
        }
        base.OnClose();
    }
}
