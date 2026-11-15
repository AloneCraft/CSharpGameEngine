using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アセットエディターのドキュメント Pane の共通部分。表示後に一度だけフレームをフローティングにする（<see cref="AssetEditorFramePlacement"/>）。
/// Ctrl+S／Z／Y は VS が WPF の KeyDown より先に消費するため、<c>PreProcessMessage</c> でも中身へ渡す。
/// </summary>
internal abstract class AssetEditorPaneBase:WindowPane,IVsWindowFrameNotify3 {
    private readonly AssetEditorFramePlacement Placement;
    private AssetEditorShell?Shell;
    protected AssetEditorPaneBase(System.IServiceProvider Services):base(Services)=>this.Placement=new AssetEditorFramePlacement(Services,()=>this.DocumentPath,()=>this.Shell);
    protected string DocumentPath { get;set;}=string.Empty;
    protected AssetEditorShell?EditorShell=>this.Shell;
    internal AssetEditorContent?EditorContent=>this.Shell?.EditorContent;
    /// <summary>復元された文書などで、保存済みの配置を尊重してフロート配置をしない。</summary>
    internal void RespectSavedPlacement()=>this.Placement.RespectSavedPlacement();
    /// <summary>派生クラスが種類固有の中身を作ってから呼ぶ。</summary>
    protected void Attach(AssetEditorShell Shell){
        this.Shell=Shell;
        this.Content=Shell;
    }
    protected override void OnCreate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnCreate();
        if(this.GetService(typeof(SVsWindowFrame)) is IVsWindowFrame Created){
            this.Placement.Attach(Created);
            ErrorHandler.ThrowOnFailure(Created.SetProperty((int)__VSFPROPID.VSFPROPID_ViewHelper,this));
        }
    }
    protected override bool PreProcessMessage(ref System.Windows.Forms.Message Message){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Shortcut=AssetToolWindowInput.Shortcut(ref Message);
        if(Shortcut!=System.Windows.Input.Key.None&&this.Shell?.EditorContent.HandleNativeShortcut(Shortcut)==true){ return true; }
        return base.PreProcessMessage(ref Message);
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
        if(Disposing){ this.Placement.Dispose(); }
        base.Dispose(Disposing);
    }
    protected void ReloadContent(string Path){
        this.DocumentPath=Path;
        this.Shell?.EditorContent.Reload(Path);
    }
}
/// <summary>
/// DocView と DocData が同一オブジェクトの読み取り専用エディター（メッシュ・テクスチャ・音声・フォント）。dirty は常に false で保存はしない。
/// <see cref="LoadFromPath"/> は、コンストラクター、<see cref="IPersistFileFormat.Load"/>、<see cref="IVsPersistDocData.LoadDocData"/> の
/// いずれをシェルが呼んでも正しいファイルを表示できるようにする（ADR-0013 と同じ形）。
/// </summary>
internal sealed class AssetEditorDocumentPane:AssetEditorPaneBase,IVsPersistDocData,IPersistFileFormat {
    internal AssetEditorDocumentPane(System.IServiceProvider Services,string FilePath,AssetEditorDescriptor Descriptor):base(Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DocumentPath=FilePath;
        this.Attach(new AssetEditorShell(AssetEditorContentFactory.Create(Descriptor,FilePath)));
    }
    private int LoadFromPath(string Path,bool ForceReload=false){
        if(!ForceReload&&string.Equals(Path,this.DocumentPath,StringComparison.OrdinalIgnoreCase)){ return VSConstants.S_OK; }
        this.ReloadContent(Path);
        return VSConstants.S_OK;
    }
    int IPersist.GetClassID(out Guid ClassId){
        ClassId=typeof(AssetEditorFactory).GUID;
        return VSConstants.S_OK;
    }
    int IPersistFileFormat.GetClassID(out Guid ClassId){
        ClassId=typeof(AssetEditorFactory).GUID;
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
    int IPersistFileFormat.InitNew(uint FormatIndex)=>VSConstants.E_NOTIMPL;
    int IPersistFileFormat.Load(string FileName,uint Mode,int ReadOnly)=>this.LoadFromPath(FileName);
    int IPersistFileFormat.Save(string FileName,int Remember,uint FormatIndex)=>VSConstants.E_NOTIMPL;
    int IPersistFileFormat.SaveCompleted(string FileName)=>VSConstants.S_OK;
    int IPersistFileFormat.GetCurFile(out string FileName,out uint FormatIndex){
        FileName=this.DocumentPath;
        FormatIndex=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.GetGuidEditorType(out Guid ClassId){
        ClassId=typeof(AssetEditorFactory).GUID;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.IsDocDataDirty(out int Dirty){
        Dirty=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.SetUntitledDocPath(string Path)=>VSConstants.E_NOTIMPL;
    int IVsPersistDocData.LoadDocData(string DocumentPath)=>this.LoadFromPath(DocumentPath);
    int IVsPersistDocData.SaveDocData(VSSAVEFLAGS SaveFlags,out string NewPath,out int SaveCanceled){
        NewPath=null!;
        SaveCanceled=0;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.Close()=>VSConstants.S_OK;
    int IVsPersistDocData.OnRegisterDocData(uint Cookie,IVsHierarchy Hierarchy,uint ItemId)=>VSConstants.S_OK;
    int IVsPersistDocData.RenameDocData(uint Attributes,IVsHierarchy Hierarchy,uint ItemId,string NewPath)=>this.LoadFromPath(NewPath);
    int IVsPersistDocData.IsDocDataReloadable(out int Reloadable){
        Reloadable=1;
        return VSConstants.S_OK;
    }
    int IVsPersistDocData.ReloadDocData(uint Flags)=>this.LoadFromPath(this.DocumentPath,ForceReload:true);
}
/// <summary>
/// 共有 JSON テキストバッファーを DocData とするエディター（.cgeasset のデータアセットエディター）。
/// 保存・dirty・Undo は VS のテキストバッファーが持ち、この Pane は DocView だけを担当する（Scene Editor と同じ形）。
/// 「JSON で開く」は同じバッファーを VS 標準のテキストエディターで開く。
/// </summary>
internal sealed class AssetEditorTextPane:AssetEditorPaneBase,IVsRunningDocTableEvents2 {
    private IVsRunningDocumentTable?RunningDocuments;
    private uint RunningDocumentsCookie;
    internal AssetEditorTextPane(System.IServiceProvider Services,string FilePath,AssetEditorDescriptor Descriptor):base(Services){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DocumentPath=FilePath;
        this.Attach(new AssetEditorShell(AssetEditorContentFactory.Create(Descriptor,FilePath)));
    }
    protected override void OnCreate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnCreate();
        if(this.GetService(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable Table){
            this.TrackRunningDocuments(Table);
        }
    }
    internal void TrackRunningDocuments(IVsRunningDocumentTable Table){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.RunningDocuments is not null){ return; }
        ErrorHandler.ThrowOnFailure(Table.AdviseRunningDocTableEvents(this,out this.RunningDocumentsCookie));
        this.RunningDocuments=Table;
    }
    public int OnAfterAttributeChangeEx(uint Cookie,uint Attributes,IVsHierarchy OldHierarchy,uint OldItemId,string OldMoniker,IVsHierarchy NewHierarchy,uint NewItemId,string NewMoniker){
        ThreadHelper.ThrowIfNotOnUIThread();
        if((Attributes&(uint)__VSRDTATTRIB.RDTA_MkDocument)!=0&&!string.IsNullOrEmpty(NewMoniker)
            &&string.Equals(OldMoniker,this.DocumentPath,StringComparison.OrdinalIgnoreCase)){
            this.ReloadContent(NewMoniker);
        }
        return VSConstants.S_OK;
    }
    public int OnAfterFirstDocumentLock(uint Cookie,uint LockType,uint ReadLocks,uint EditLocks)=>VSConstants.S_OK;
    public int OnBeforeLastDocumentUnlock(uint Cookie,uint LockType,uint ReadLocks,uint EditLocks)=>VSConstants.S_OK;
    public int OnAfterSave(uint Cookie)=>VSConstants.S_OK;
    public int OnAfterAttributeChange(uint Cookie,uint Attributes)=>VSConstants.S_OK;
    public int OnBeforeDocumentWindowShow(uint Cookie,int FirstShow,IVsWindowFrame Frame)=>VSConstants.S_OK;
    public int OnAfterDocumentWindowHide(uint Cookie,IVsWindowFrame Frame)=>VSConstants.S_OK;
    protected override void Dispose(bool Disposing){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Disposing&&this.RunningDocuments is{ }Table){
            Table.UnadviseRunningDocTableEvents(this.RunningDocumentsCookie);
            this.RunningDocuments=null;
            this.RunningDocumentsCookie=0;
        }
        base.Dispose(Disposing);
    }
}
