using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// 参照ビューアーのドキュメントウィンドウ。DocView と DocData が同一の読み取り専用 Pane で、保存するファイルを持たない。
/// 表示後に一度だけ、他のアセットエディターと同じ <see cref="AssetEditorFramePlacement"/> で前面のフローティングウィンドウにする。
/// </summary>
internal sealed class AssetReferenceViewerWindowPane:WindowPane,IVsPersistDocData,IPersistFileFormat,IVsWindowFrameNotify3 {
    /// <summary>この文書を作るエディターを表す GUID。エディターファクトリーは登録せず、<see cref="AssetReferenceViewerService"/> がフレームを直接作る。</summary>
    internal static readonly Guid EditorGuid=new("5d0c7a4e-8b2f-4c63-9e17-3a6f1d2b8c94");
    private readonly AssetReferenceViewerControl Control;
    private readonly AssetEditorFramePlacement Placement;
    private string Title;
    internal AssetReferenceViewerWindowPane(System.IServiceProvider Services,AssetReferenceViewerControl Control,string Title):base(Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Control=Control;
        this.Title=Title;
        this.Placement=new AssetEditorFramePlacement(Services,()=>this.Title,()=>this.Control);
        this.Content=Control;
    }
    internal AssetReferenceViewerControl ViewerControl=>this.Control;
    protected override void OnCreate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnCreate();
        if(this.GetService(typeof(SVsWindowFrame)) is IVsWindowFrame Created){
            this.Placement.Attach(Created);
            ErrorHandler.ThrowOnFailure(Created.SetProperty((int)__VSFPROPID.VSFPROPID_ViewHelper,this));
        }
    }
    internal void ShowFrame(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.GetService(typeof(SVsWindowFrame)) is IVsWindowFrame Frame){ ErrorHandler.ThrowOnFailure(Frame.Show()); }
    }
    internal void SetTitle(string Title){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Title=Title;
        if(this.GetService(typeof(SVsWindowFrame)) is IVsWindowFrame Frame){ Frame.SetProperty((int)__VSFPROPID.VSFPROPID_EditorCaption,Title); }
    }
    public int OnShow(int Show){
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
    protected override void Dispose(bool Disposing){
        if(Disposing){
            this.Placement.Dispose();
            this.Control.Dispose();
            AssetReferenceViewerService.OnPaneDisposed(this);
        }
        base.Dispose(Disposing);
    }
    int IPersist.GetClassID(out Guid ClassId){
        ClassId=EditorGuid;
        return VSConstants.S_OK;
    }
    int IPersistFileFormat.GetClassID(out Guid ClassId){
        ClassId=EditorGuid;
        return VSConstants.S_OK;
    }
    int IPersistFileFormat.GetFormatList(out string FormatList){
        FormatList=string.Empty;
        return VSConstants.S_OK;
    }
    int IPersistFileFormat.IsDirty(out int Dirty){
        Dirty=0;
        return VSConstants.S_OK;
    }
    int IPersistFileFormat.InitNew(uint FormatIndex)=>VSConstants.S_OK;
    int IPersistFileFormat.Load(string FileName,uint Mode,int ReadOnly)=>VSConstants.S_OK;
    int IPersistFileFormat.Save(string FileName,int Remember,uint FormatIndex)=>VSConstants.E_NOTIMPL;
    int IPersistFileFormat.SaveCompleted(string FileName)=>VSConstants.S_OK;
    int IPersistFileFormat.GetCurFile(out string FileName,out uint FormatIndex){
        FileName=this.Title;
        FormatIndex=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.GetGuidEditorType(out Guid ClassId){
        ClassId=EditorGuid;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.IsDocDataDirty(out int Dirty){
        Dirty=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.SetUntitledDocPath(string Path)=>VSConstants.S_OK;
    int IVsPersistDocData.LoadDocData(string DocumentPath)=>VSConstants.S_OK;
    int IVsPersistDocData.SaveDocData(VSSAVEFLAGS SaveFlags,out string NewPath,out int SaveCanceled){
        NewPath=null!;
        SaveCanceled=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.Close()=>VSConstants.S_OK;
    int IVsPersistDocData.OnRegisterDocData(uint Cookie,IVsHierarchy Hierarchy,uint ItemId)=>VSConstants.S_OK;
    int IVsPersistDocData.RenameDocData(uint Attributes,IVsHierarchy Hierarchy,uint ItemId,string NewPath)=>VSConstants.S_OK;
    int IVsPersistDocData.IsDocDataReloadable(out int Reloadable){
        Reloadable=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.ReloadDocData(uint Flags)=>VSConstants.S_OK;
}
/// <summary>
/// 「参照関係」から参照ビューアーを開く。開いていれば同じウィンドウを前面に出して起点だけ切り替え、なければ新しいドキュメントウィンドウを作る。
/// </summary>
internal static class AssetReferenceViewerService {
    private static AssetReferenceViewerWindowPane?Current;
    internal static void OnPaneDisposed(AssetReferenceViewerWindowPane Pane){
        if(ReferenceEquals(Current,Pane)){ Current=null; }
    }
    internal static string Caption(AssetWorkspaceItem Item)=>VsixText.Format("ReferenceViewer_Title",System.IO.Path.GetFileName(Item.Source.SourcePath));
    internal static void Open(System.IServiceProvider Services,AssetWorkspaceItem Item){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Current is{ }Existing){
            Existing.ViewerControl.Show(Item);
            Existing.SetTitle(Caption(Item));
            Existing.ShowFrame();
            return;
        }
        var Control=new AssetReferenceViewerControl(BuildGraphAsync,Resolve);
        Control.OpenRequested+=Path=>OpenFile(Services,Path);
        var Pane=new AssetReferenceViewerWindowPane(Services,Control,Caption(Item));
        if(Services.GetService(typeof(SVsUIShell)) is not IVsUIShell Shell){ throw new InvalidOperationException(VsixText.Get("AssetEditor_NavigationUnavailable")); }
        var Document="cge-references://"+Item.Source.ProjectPath.Replace('\\','/');
        var EditorType=AssetReferenceViewerWindowPane.EditorGuid;
        var CommandUi=Guid.Empty;
        var DocView=Marshal.GetIUnknownForObject(Pane);
        var DocData=Marshal.GetIUnknownForObject(Pane);
        try {
            ErrorHandler.ThrowOnFailure(Shell.CreateDocumentWindow((uint)__VSCREATEDOCWIN.CDW_fCreateNewWindow,Document,null,VSConstants.VSITEMID_NIL,DocView,DocData,
                ref EditorType,string.Empty,ref CommandUi,null,Caption(Item),string.Empty,null,out var Frame));
            Current=Pane;
            Control.Show(Item);
            ErrorHandler.ThrowOnFailure(Frame.Show());
        }finally {
            Marshal.Release(DocView);
            Marshal.Release(DocData);
        }
    }
    private static AssetWorkspaceItem?Resolve(string Path){
        ThreadHelper.ThrowIfNotOnUIThread();
        return (AssetsToolWindowPane.Controller?.Items??[]).FirstOrDefault(Item=>string.Equals(Item.Source.SourcePath,Path,StringComparison.OrdinalIgnoreCase));
    }
    private static void OpenFile(System.IServiceProvider Services,string Path){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(System.IO.File.Exists(Path)){ VsShellUtilities.OpenDocument(Services,Path); }
    }
    private static async System.Threading.Tasks.Task<AssetReferenceGraph> BuildGraphAsync(AssetWorkspaceItem Item,int Referencers,int Dependencies,System.Threading.CancellationToken Token){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Token);
        var Items=AssetsToolWindowPane.Controller?.Items??[];
        return await AssetReferenceGraphBuilder.BuildAsync(Item,Items,Referencers,Dependencies,FindReferencersAsync,ReadText,Token);
    }
    private static async System.Threading.Tasks.Task<IReadOnlyList<string>> FindReferencersAsync(AssetWorkspaceItem Target,System.Threading.CancellationToken Token){
        var Plan=await AssetReferencePlanner.PlanAsync(Target,Target.Source.SourcePath,AssetFileOperation.Inspect,Token);
        return Plan.Edits.Select(static Edit=>Edit.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string?ReadText(string Path){
        try { return System.IO.File.ReadAllText(Path); }
        catch(Exception Error)when(Error is System.IO.IOException or UnauthorizedAccessException){ return null; }
    }
}
