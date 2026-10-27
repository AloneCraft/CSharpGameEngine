using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
internal static class AssetEditorNavigation {
    internal static string ShowInSolution(IServiceProvider Services,string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Services.GetService(typeof(SVsUIShellOpenDocument)) is not IVsUIShellOpenDocument Documents){ return VsixText.Get("AssetEditor_NavigationUnavailable"); }
        ErrorHandler.ThrowOnFailure(Documents.IsDocumentInAProject(FilePath,out var Hierarchy,out var ItemId,out _,out var Found));
        // ExtFilesProjectは「その他のファイル」で、ソリューション内のプロジェクトではない。
        if(Found!=(int)__VSDOCINPROJECT.DOCINPROJ_DocInProject||Hierarchy is null||ItemId==VSConstants.VSITEMID_NIL){ return VsixText.Get("AssetEditor_NotInSolution"); }
        if(Services.GetService(typeof(SVsUIShell)) is not IVsUIShell Shell){ return VsixText.Get("AssetEditor_NavigationUnavailable"); }
        var Explorer=VSConstants.StandardToolWindows.SolutionExplorer;
        ErrorHandler.ThrowOnFailure(Shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate,ref Explorer,out var Frame));
        if(Frame is null){ return VsixText.Get("AssetEditor_NavigationUnavailable"); }
        ErrorHandler.ThrowOnFailure(Frame.Show());
        ErrorHandler.ThrowOnFailure(Frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView,out var View));
        if(View is not IVsUIHierarchyWindow Window){ return VsixText.Get("AssetEditor_NavigationUnavailable"); }
        ErrorHandler.ThrowOnFailure(Window.ExpandItem(Hierarchy,ItemId,EXPANDFLAGS.EXPF_ExpandParentsToShowItem));
        ErrorHandler.ThrowOnFailure(Window.ExpandItem(Hierarchy,ItemId,EXPANDFLAGS.EXPF_SelectItem));
        return VsixText.Get("AssetEditor_RevealedInSolution");
    }
}
