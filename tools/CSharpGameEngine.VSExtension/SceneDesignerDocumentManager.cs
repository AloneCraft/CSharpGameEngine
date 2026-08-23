using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

internal sealed class SceneDesignerDocumentManager:IDisposable {
    private const int ENoInterface=unchecked((int)0x80004002);
    private readonly IServiceProvider ServiceProvider;
    private readonly Dictionary<SceneKey,SceneDesignerDocumentPane>Panes=[];
    private bool Disposed;

    internal SceneDesignerDocumentManager(IServiceProvider ServiceProvider)=>
        this.ServiceProvider=ServiceProvider??throw new ArgumentNullException(nameof(ServiceProvider));

    internal void Open(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){ throw new ObjectDisposedException(nameof(SceneDesignerDocumentManager)); }
        if(this.Panes.TryGetValue(Snapshot.Key,out SceneDesignerDocumentPane? Existing)){
            Existing.SetTarget(Session,Snapshot);
            Existing.Activate();
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
        var Pane=new SceneDesignerDocumentPane(this.ServiceProvider,Snapshot.Key,this.OnPaneClosed);
        try {
            CreateFrame(Pane,Snapshot);
            Pane.SetTarget(Session,Snapshot);
            this.Panes.Add(Snapshot.Key,Pane);
            Pane.Activate();
        } catch {
            Pane.Dispose();
            throw;
        }
    }

    private void CreateFrame(SceneDesignerDocumentPane Pane,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneDesignerDocumentWindowRequest Request=SceneDesignerDocumentWindowPolicy.CreateRequest(Snapshot.Key);
        if(this.ServiceProvider.GetService(SceneDesignerDocumentWindowPolicy.HostServiceType)
            is not IVsExternalFilesManager ExternalFiles){
            throw SceneDesignerDocumentWindowPolicy.CreateFailure(
                "SVsExternalFilesManager.GetService",Request.Moniker,ENoInterface);
        }

        IntPtr View=IntPtr.Zero;
        try {
            View=Marshal.GetIUnknownForObject((IVsWindowPane)Pane);
            Guid Editor=Request.EditorTypeGuid;
            Guid CommandUi=Request.CommandUiGuid;
            int DefaultPosition;
            int HResult=ExternalFiles.AddDocument(
                Request.Flags,
                Request.Moniker,
                View,
                IntPtr.Zero,
                ref Editor,
                Request.PhysicalView,
                ref CommandUi,
                Request.OwnerCaption,
                Request.EditorCaption,
                out DefaultPosition,
                out IVsWindowFrame Frame);
            SceneDesignerDocumentWindowPolicy.EnsureAddDocumentSucceeded(Request,HResult,Frame);
            Pane.AttachFrame(Frame);
        } catch(SceneDesignerDocumentWindowException){
            throw;
        } catch(Exception Ex){
            throw SceneDesignerDocumentWindowPolicy.CreateFailure(
                "IVsExternalFilesManager.AddDocument",Request.Moniker,Ex.HResult,Ex);
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
}

internal sealed class SceneDesignerDocumentPane:WindowPane {
    private readonly SceneKey Key;
    private readonly Action<SceneKey>Closed;
    private readonly SceneDesignerToolWindowControl Control;
    private IVsWindowFrame?Frame;
    private bool IsClosed;

    internal SceneDesignerDocumentPane(IServiceProvider ServiceProvider,SceneKey Key,Action<SceneKey>Closed):base(ServiceProvider){
        this.Key=Key;
        this.Closed=Closed??throw new ArgumentNullException(nameof(Closed));
        this.Control=new SceneDesignerToolWindowControl();
        this.Content=this.Control;
    }

    internal void AttachFrame(IVsWindowFrame Frame)=>this.Frame=Frame??throw new ArgumentNullException(nameof(Frame));
    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Control.SetTarget(Session,Snapshot);
    }
    internal void Activate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Frame is null){
            throw new InvalidOperationException("The Scene Designer document frame is unavailable.");
        }
        ErrorHandler.ThrowOnFailure(this.Frame.Show());
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
