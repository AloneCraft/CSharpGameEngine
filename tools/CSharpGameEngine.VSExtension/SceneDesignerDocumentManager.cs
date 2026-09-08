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
                SceneDesignerDocumentWindowPolicy.SelectDocData(View),
                ref Editor,
                Request.PhysicalView!,
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
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.CloseAll();
    }

    internal void CloseAll(){
        ThreadHelper.ThrowIfNotOnUIThread();
        var OpenPanes=new List<SceneDesignerDocumentPane>(this.Panes.Values);
        foreach(SceneDesignerDocumentPane Pane in OpenPanes){ Pane.Close(); }
        this.Panes.Clear();
    }

    internal void ReleaseRuntimeResourcesForSolutionClose(){
        ThreadHelper.ThrowIfNotOnUIThread();
        // The shell owns the external document frames and is already closing them. Re-entering
        // IVsWindowFrame.CloseFrame from a solution-close callback deadlocks that same close.
        // Release only engine-owned controls/Preview Hosts here and let the shell finish its
        // frame lifecycle.
        var OpenPanes=new List<SceneDesignerDocumentPane>(this.Panes.Values);
        foreach(SceneDesignerDocumentPane Pane in OpenPanes){
            Pane.ReleaseRuntimeResources();
        }
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

    internal void Close(){
        ThreadHelper.ThrowIfNotOnUIThread();
        IVsWindowFrame?OpenFrame=this.Frame;
        this.Frame=null;
        try {
            if(OpenFrame is not null){
                ActivityLog.LogInformation(
                    nameof(SceneDesignerDocumentPane),
                    "Closing Visual Studio-owned Scene document frame.");
                int HResult=OpenFrame.CloseFrame(
                    (uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                ActivityLog.LogInformation(
                    nameof(SceneDesignerDocumentPane),
                    "Scene document frame close returned HRESULT=0x"
                    +HResult.ToString("X8")+".");
                ErrorHandler.ThrowOnFailure(HResult);
            }
        } finally {
            // CloseFrame normally calls OnClose. Keep disposal as the idempotent fallback for
            // incomplete shell teardown and for panes whose frame was never attached.
            this.Dispose();
        }
    }

    internal void ReleaseRuntimeResources(){
        ThreadHelper.ThrowIfNotOnUIThread();
        ActivityLog.LogInformation(
            nameof(SceneDesignerDocumentPane),
            "Releasing Scene document runtime resources before shell frame close.");
        this.CloseCore();
    }

    internal static string CreateCaption(SceneKey Key){
        string Name=Key.ClassFullyQualifiedName;
        int LastSeparator=Name.LastIndexOf('.');
        return (LastSeparator>=0?Name.Substring(LastSeparator+1):Name)+" [Scene]";
    }

    protected override void OnClose(){
        this.Frame=null;
        this.CloseCore();
        base.OnClose();
    }

    protected override void Dispose(bool Disposing){
        if(Disposing){ this.CloseCore(); }
        base.Dispose(Disposing);
    }

    private void CloseCore(){
        if(!this.IsClosed){
            this.IsClosed=true;
            this.Closed(this.Key);
            this.Control.Dispose();
        }
    }
}
