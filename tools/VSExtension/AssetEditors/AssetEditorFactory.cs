using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// アセットをダブルクリックしたときに UE5 風の専用エディターを開くファクトリ。対象は <see cref="AssetEditorRegistry"/> の拡張子
/// （メッシュ・テクスチャ・音声・フォント・データアセット）。対応表にない拡張子は <c>VS_E_UNSUPPORTEDFORMAT</c> で辞退し、VS が次のエディターに渡す。
/// シーン JSON とシェーダーグラフは別のエディターが担当する。
/// </summary>
[Guid(FactoryGuidString)]
internal sealed class AssetEditorFactory:IVsEditorFactory {
    public const string FactoryGuidString="3b8e5f40-6c1d-4f6a-9d0e-7a2b4c9d1e53";
    private readonly System.IServiceProvider Package;
    private IOleServiceProvider?Site;
    internal AssetEditorFactory(System.IServiceProvider Package)=>this.Package=Package??throw new ArgumentNullException(nameof(Package));
    public int SetSite(IOleServiceProvider ServiceProvider){
        this.Site=ServiceProvider;
        return VSConstants.S_OK;
    }
    public int Close()=>VSConstants.S_OK;
    public int CreateEditorInstance(
        uint CreateFlags,string DocumentPath,string PhysicalView,IVsHierarchy Hierarchy,uint ItemId,
        IntPtr ExistingDocData,out IntPtr DocView,out IntPtr DocData,out string Caption,out Guid CommandUi,
        out int CreateDocumentWindowFlags){
        ThreadHelper.ThrowIfNotOnUIThread();
        DocView=IntPtr.Zero;
        DocData=IntPtr.Zero;
        Caption=string.Empty;
        CommandUi=Guid.Empty;
        CreateDocumentWindowFlags=0;
        if(!AssetEditorRegistry.TryGet(DocumentPath,out var Descriptor)){ return VSConstants.VS_E_UNSUPPORTEDFORMAT; }
        ActivityLog.LogInformation(nameof(AssetEditorFactory),$"Asset editor create '{DocumentPath}': flags=0x{CreateFlags:X}, view='{PhysicalView}', itemId=0x{ItemId:X}, hierarchy={(Hierarchy is null?"null":"set")}, existingDocData={(ExistingDocData==IntPtr.Zero?"no":"yes")}.");
        try {
            if(Descriptor.UsesTextBuffer){ return this.CreateTextEditor(DocumentPath,Descriptor,CreateFlags,ExistingDocData,out DocView,out DocData,out CommandUi); }
            // 読み取り専用のエディターは他の DocData を引き継げないので、常に新しく作る。
            if(ExistingDocData!=IntPtr.Zero){ return VSConstants.VS_E_INCOMPATIBLEDOCDATA; }
            var Pane=new AssetEditorDocumentPane(this.Package,DocumentPath,Descriptor);
            if(AssetEditorFramePlacement.IsShellRestoreOrSpecificEditor(CreateFlags)){ Pane.RespectSavedPlacement(); }
            DocView=Marshal.GetIUnknownForObject(Pane);
            DocData=Marshal.GetIUnknownForObject(Pane);
            ActivityLog.LogInformation(nameof(AssetEditorFactory),$"Asset editor opened '{DocumentPath}' as {Descriptor.Kind}.");
            return VSConstants.S_OK;
        } catch(Exception Error){
            ActivityLog.LogError(nameof(AssetEditorFactory),$"Asset editor creation failed for '{DocumentPath}': {Error}");
            if(DocView!=IntPtr.Zero){ Marshal.Release(DocView);DocView=IntPtr.Zero; }
            if(DocData!=IntPtr.Zero){ Marshal.Release(DocData);DocData=IntPtr.Zero; }
            return VSConstants.E_FAIL;
        }
    }
    private int CreateTextEditor(string DocumentPath,AssetEditorDescriptor Descriptor,uint CreateFlags,IntPtr ExistingDocData,out IntPtr DocView,out IntPtr DocData,out Guid CommandUi){
        ThreadHelper.ThrowIfNotOnUIThread();
        DocView=IntPtr.Zero;
        DocData=IntPtr.Zero;
        CommandUi=VSConstants.GUID_TextEditorFactory;
        IVsTextLines?Existing=null;
        if(ExistingDocData!=IntPtr.Zero&&(Existing=SharedBuffer(Marshal.GetObjectForIUnknown(ExistingDocData))) is null){ return VSConstants.VS_E_INCOMPATIBLEDOCDATA; }
        var Restored=AssetEditorFramePlacement.IsShellRestoreOrSpecificEditor(CreateFlags);
        var Buffer=Existing??this.CreateBuffer();
        var Pane=new AssetEditorTextPane(this.Package,DocumentPath,Descriptor);
        if(Restored){ Pane.RespectSavedPlacement(); }
        DocView=Marshal.GetIUnknownForObject(Pane);
        DocData=Marshal.GetIUnknownForObject(Buffer);
        ActivityLog.LogInformation(nameof(AssetEditorFactory),$"Asset editor opened '{DocumentPath}' as {Descriptor.Kind}; shared buffer={Existing is not null}.");
        return VSConstants.S_OK;
    }
    /// <summary>別のエディターが先に開いた DocData のテキストバッファー。テキストでなければ null。</summary>
    private static IVsTextLines?SharedBuffer(object DocData){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(DocData is IVsTextLines Lines){ return Lines; }
        return DocData is IVsTextBufferProvider Provider&&Provider.GetTextBuffer(out var Provided)==VSConstants.S_OK?Provided:null;
    }
    private IVsTextLines CreateBuffer(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Package.GetService(typeof(SComponentModel)) is not IComponentModel ComponentModel||this.Site is null){
            throw new InvalidOperationException("The editor services are unavailable.");
        }
        var Adapters=ComponentModel.GetService<IVsEditorAdaptersFactoryService>();
        var Buffer=(IVsTextLines)Adapters.CreateVsTextBufferAdapter(this.Site);
        var Language=SceneJsonEditorFactory.JsonLanguageService;
        ErrorHandler.ThrowOnFailure(Buffer.SetLanguageServiceID(ref Language));
        return Buffer;
    }
    public int MapLogicalView(ref Guid LogicalView,out string PhysicalView){
        PhysicalView=null!;
        return LogicalView==VSConstants.LOGVIEWID_Primary||LogicalView==VSConstants.LOGVIEWID_Designer?VSConstants.S_OK:VSConstants.E_NOTIMPL;
    }
}
