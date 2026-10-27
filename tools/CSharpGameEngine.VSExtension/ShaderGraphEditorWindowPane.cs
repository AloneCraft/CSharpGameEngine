using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using CSharpGameEngine.ShaderGraph;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
#pragma warning disable VSTHRD010,VSTHRD001
internal sealed class ShaderGraphEditorWindowPane:WindowPane,IVsPersistDocData,IPersistFileFormat,IOleCommandTarget,IVsWindowFrameNotify3 {
    private readonly ShaderGraphEditorControl Control;
    private readonly FileSystemWatcher Watcher;
    private readonly ErrorListProvider ErrorList;
    private ShaderGraphEditorDocument Document;
    private bool OwnSave;
    private int ExternalChangeTicket;
    private readonly AssetEditorFramePlacement Placement;
    internal ShaderGraphEditorWindowPane(string FilePath) {
        this.Document=ShaderGraphEditorDocument.Load(FilePath);
        this.Placement=new AssetEditorFramePlacement(ServiceProvider.GlobalProvider,()=>this.Document.FilePath,()=>this.Control);
        this.Control=new ShaderGraphEditorControl(this.Document);
        this.Control.SaveRequested+=this.Save;
        this.Control.OpenGeneratedHlslRequested+=this.OpenGeneratedHlsl;
        this.Control.OpenDependencyRequested+=this.OpenDependency;
        this.Content=this.Control;
        this.ErrorList=new ErrorListProvider(this);
        this.Document.Changed+=this.UpdateErrorList;
        var FullPath=Path.GetFullPath(FilePath);
        this.Watcher=new FileSystemWatcher(Path.GetDirectoryName(FullPath)!,Path.GetFileName(FullPath)) { NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size,EnableRaisingEvents=true };
        this.Watcher.Changed+=this.OnExternalChange;
        this.Watcher.Created+=this.OnExternalChange;
        this.Watcher.Renamed+=this.OnExternalRename;
        this.UpdateErrorList();
    }
    private int LoadFromPath(string PathValue) {
        if(!string.Equals(Path.GetFullPath(PathValue),this.Document.FilePath,StringComparison.OrdinalIgnoreCase)) { return VSConstants.E_NOTIMPL; }
        this.Document.Reload();
        return VSConstants.S_OK;
    }
    private void Save() {
        this.OwnSave=true;
        try {
            this.Document.Save();
            var Result=ShaderGraphEditorBuildService.BuildAndPublish(this.Document.FilePath);
            this.SetStatus(Result.Message);
        } catch(Exception Exception) {
            this.SetStatus(Exception.Message);
        } finally {
            this.OwnSave=false;
        }
    }
    private void OpenGeneratedHlsl() {
        var Result=ShaderGraphEditorBuildService.BuildAndPublish(this.Document.FilePath);
        this.SetStatus(Result.Message);
        if(Result.Success&&Result.GeneratedHlslPath is { }PathValue&&File.Exists(PathValue)) {
            VsShellUtilities.OpenDocument(this,PathValue);
        }
    }
    private void OpenDependency(string RelativePath) {
        try {
            var FullPath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(this.Document.FilePath)!,RelativePath));
            if(!File.Exists(FullPath)) { this.SetStatus(VsixText.Format("ShaderGraphDependencyMissing",FullPath));return; }
            VsShellUtilities.OpenDocument(this,FullPath);
        } catch(Exception Exception) { this.SetStatus(Exception.Message); }
    }    private void SetStatus(string Text)=>this.Control.SetStatusText(Text);
    private void UpdateErrorList() {
        this.ErrorList.Tasks.Clear();
        foreach(var Diagnostic in this.Document.Validate().Diagnostics) {
            ErrorTask Task=new() { Document=this.Document.FilePath,Text=Diagnostic.Message,Category=TaskCategory.BuildCompile,ErrorCategory=Diagnostic.Severity==ShaderGraphDiagnosticSeverity.Error?TaskErrorCategory.Error:TaskErrorCategory.Warning };
            if(Diagnostic.NodeId is { }NodeId) { Task.Navigate+=(Sender,Arguments)=>this.SetStatus(VsixText.Format("ShaderGraphNodeDiagnostic",NodeId,Diagnostic.Message)); }
            this.ErrorList.Tasks.Add(Task);
        }
    }
    private void OnExternalChange(object Sender,FileSystemEventArgs Arguments)=>this.HandleExternalChange();
    private void OnExternalRename(object Sender,RenamedEventArgs Arguments)=>this.HandleExternalChange();
    private void HandleExternalChange() {
        if(!File.Exists(this.Document.FilePath)) { return; }
        var Ticket=Interlocked.Increment(ref this.ExternalChangeTicket);
        _=this.Control.Dispatcher.BeginInvoke(new Action(()=>{
            var Timer=new DispatcherTimer(DispatcherPriority.Background,this.Control.Dispatcher) { Interval=TimeSpan.FromMilliseconds(200) };
            Timer.Tick+=(Sender,Arguments)=>{Timer.Stop();this.QueueExternalChange(Ticket,0);};
            Timer.Start();
        }));
    }
    private void QueueExternalChange(int Ticket,int Attempt) {
        _=this.Control.Dispatcher.BeginInvoke(new Action(()=>{
            if(Ticket!=Volatile.Read(ref this.ExternalChangeTicket)||this.OwnSave||!File.Exists(this.Document.FilePath)) { return; }
            try {
                if(!this.Document.IsDirty) { this.Document.Reload();this.SetStatus(VsixText.Get("ShaderGraphReloaded"));return; }
                var Result=System.Windows.MessageBox.Show(VsixText.Get("ShaderGraphExternalChangePrompt"),VsixText.Get("ShaderGraphExternalChangeTitle"),System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Warning);
                if(Result==System.Windows.MessageBoxResult.Yes) { this.Document.Reload(); }
                else { this.SetStatus(VsixText.Get("ShaderGraphExternalKeep")); }
            } catch(Exception Exception) when(Exception is IOException||Exception is JsonException) {
                if(Attempt>=20) { this.SetStatus(Exception.Message);return; }
                var Timer=new DispatcherTimer(DispatcherPriority.Background,this.Control.Dispatcher) { Interval=TimeSpan.FromMilliseconds(100) };
                Timer.Tick+=(Sender,Arguments)=>{Timer.Stop();this.QueueExternalChange(Ticket,Attempt+1);};
                Timer.Start();
            }
        }));
    }
    int IPersist.GetClassID(out Guid ClassId) { ClassId=typeof(ShaderGraphEditorFactory).GUID;return VSConstants.S_OK; }
    int IPersistFileFormat.GetClassID(out Guid ClassId) { ClassId=typeof(ShaderGraphEditorFactory).GUID;return VSConstants.S_OK; }
    int IPersistFileFormat.GetFormatList(out string FormatList) { FormatList="Shader Graph (*.cgeshadergraph;*.cgesubgraph)\n*.cgeshadergraph;*.cgesubgraph\n";return VSConstants.S_OK; }
    int IPersistFileFormat.IsDirty(out int Dirty) { Dirty=this.Document.IsDirty?1:0;return VSConstants.S_OK; }
    int IPersistFileFormat.InitNew(uint FormatIndex)=>VSConstants.E_NOTIMPL;
    int IPersistFileFormat.Load(string FileName,uint Mode,int ReadOnly)=>this.LoadFromPath(FileName);
    int IPersistFileFormat.Save(string FileName,int Remember,uint FormatIndex) { if(!string.IsNullOrWhiteSpace(FileName)){this.Document.Save(FileName);}else{this.Save();}return VSConstants.S_OK; }
    int IPersistFileFormat.SaveCompleted(string FileName)=>VSConstants.S_OK;
    int IPersistFileFormat.GetCurFile(out string FileName,out uint FormatIndex) { FileName=this.Document.FilePath;FormatIndex=0;return VSConstants.S_OK; }
    int IVsPersistDocData.GetGuidEditorType(out Guid ClassId) { ClassId=typeof(ShaderGraphEditorFactory).GUID;return VSConstants.S_OK; }
    int IVsPersistDocData.IsDocDataDirty(out int Dirty) { Dirty=this.Document.IsDirty?1:0;return VSConstants.S_OK; }
    int IVsPersistDocData.SetUntitledDocPath(string PathValue)=>VSConstants.E_NOTIMPL;
    int IVsPersistDocData.LoadDocData(string DocumentPath)=>this.LoadFromPath(DocumentPath);
    int IVsPersistDocData.SaveDocData(VSSAVEFLAGS SaveFlags,out string NewPath,out int SaveCanceled) { this.Save();NewPath=this.Document.FilePath;SaveCanceled=0;return VSConstants.S_OK; }
    int IVsPersistDocData.Close()=>VSConstants.S_OK;
    int IVsPersistDocData.OnRegisterDocData(uint Cookie,IVsHierarchy Hierarchy,uint ItemId)=>VSConstants.S_OK;
    int IVsPersistDocData.RenameDocData(uint Attributes,IVsHierarchy Hierarchy,uint ItemId,string NewPath)=>this.LoadFromPath(NewPath);
    int IVsPersistDocData.IsDocDataReloadable(out int Reloadable) { Reloadable=1;return VSConstants.S_OK; }
    int IVsPersistDocData.ReloadDocData(uint Flags) { this.Document.Reload();return VSConstants.S_OK; }
    int IOleCommandTarget.QueryStatus(ref Guid CommandGroup,uint CommandCount,OLECMD[]Commands,IntPtr CommandText) {
        if(CommandGroup!=VSConstants.GUID_VSStandardCommandSet97||CommandCount==0) { return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP; }
        for(var Index=0;Index<Commands.Length;Index++) {
            if(Commands[Index].cmdID==(uint)VSConstants.VSStd97CmdID.Undo) { Commands[Index].cmdf=(uint)(OLECMDF.OLECMDF_SUPPORTED|(this.Document.CanUndo?OLECMDF.OLECMDF_ENABLED:0)); }
            else if(Commands[Index].cmdID==(uint)VSConstants.VSStd97CmdID.Redo) { Commands[Index].cmdf=(uint)(OLECMDF.OLECMDF_SUPPORTED|(this.Document.CanRedo?OLECMDF.OLECMDF_ENABLED:0)); }
            else if(Commands[Index].cmdID==(uint)VSConstants.VSStd97CmdID.SaveProjectItem) { Commands[Index].cmdf=(uint)(OLECMDF.OLECMDF_SUPPORTED|OLECMDF.OLECMDF_ENABLED); }
        }
        return VSConstants.S_OK;
    }
    int IOleCommandTarget.Exec(ref Guid CommandGroup,uint CommandId,uint Options,IntPtr Input,IntPtr Output) {
        if(CommandGroup!=VSConstants.GUID_VSStandardCommandSet97) { return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP; }
        if(CommandId==(uint)VSConstants.VSStd97CmdID.Undo) { return this.Document.Undo()?VSConstants.S_OK:VSConstants.S_FALSE; }
        if(CommandId==(uint)VSConstants.VSStd97CmdID.Redo) { return this.Document.Redo()?VSConstants.S_OK:VSConstants.S_FALSE; }
        if(CommandId==(uint)VSConstants.VSStd97CmdID.SaveProjectItem) { this.Save();return VSConstants.S_OK; }
        return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }
    /// <summary>復元された文書などで、保存済みの配置を尊重してフロート配置をしない。</summary>
    internal void RespectSavedPlacement()=>this.Placement.RespectSavedPlacement();
    /// <summary>シェーダーグラフ（UE5 のマテリアルエディター相当）も、他のアセットエディターと同じく既定で前面のフローティングウィンドウに開く（M0127）。</summary>
    protected override void OnCreate() {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnCreate();
        if(this.GetService(typeof(SVsWindowFrame)) is IVsWindowFrame Created) {
            this.Placement.Attach(Created);
            ErrorHandler.ThrowOnFailure(Created.SetProperty((int)__VSFPROPID.VSFPROPID_ViewHelper,this));
        }
    }
    public int OnShow(int Show) {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Placement.OnShow(Show);
        return VSConstants.S_OK;
    }
    public int OnMove(int X,int Y,int Width,int Height)=>VSConstants.S_OK;
    public int OnSize(int X,int Y,int Width,int Height)=>VSConstants.S_OK;
    public int OnDockableChange(int Docked,int X,int Y,int Width,int Height){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Placement.OnHostingChanged();
        return VSConstants.S_OK;
    }
    public int OnClose(ref uint Options)=>VSConstants.S_OK;
    protected override void Dispose(bool Disposing) {
        if(Disposing) {
            this.Placement.Dispose();
            this.Watcher.EnableRaisingEvents=false;
            this.Watcher.Dispose();
            this.ErrorList.Dispose();
        }
        base.Dispose(Disposing);
    }
}
