using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
[Guid(FactoryGuidString)]
internal sealed class ShaderGraphEditorFactory:IVsEditorFactory {
    public const string FactoryGuidString="8f455350-9f37-4e2e-9f61-928e1f580060";
    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider ServiceProvider)=>VSConstants.S_OK;
    public int Close()=>VSConstants.S_OK;
    public int CreateEditorInstance(uint CreateFlags,string DocumentPath,string PhysicalView,IVsHierarchy Hierarchy,uint ItemId,IntPtr ExistingDocData,out IntPtr DocView,out IntPtr DocData,out string Caption,out Guid CommandUi,out int CreateDocumentWindowFlags) {
        DocView=IntPtr.Zero;
        DocData=IntPtr.Zero;
        Caption=string.Empty;
        CommandUi=Guid.Empty;
        CreateDocumentWindowFlags=0;
        if(ExistingDocData!=IntPtr.Zero) { return VSConstants.VS_E_INCOMPATIBLEDOCDATA; }
        try {
            ShaderGraphEditorWindowPane Pane=new(DocumentPath);
            if(AssetEditorFramePlacement.IsShellRestoreOrSpecificEditor(CreateFlags)) { Pane.RespectSavedPlacement(); }
            DocView=Marshal.GetIUnknownForObject(Pane);
            DocData=Marshal.GetIUnknownForObject(Pane);
            return VSConstants.S_OK;
        } catch(Exception) {
            return VSConstants.E_FAIL;
        }
    }
    public int MapLogicalView(ref Guid LogicalView,out string PhysicalView) {
        PhysicalView=null!;
        return LogicalView==VSConstants.LOGVIEWID_Primary||LogicalView==VSConstants.LOGVIEWID_Designer?VSConstants.S_OK:VSConstants.E_NOTIMPL;
    }
}