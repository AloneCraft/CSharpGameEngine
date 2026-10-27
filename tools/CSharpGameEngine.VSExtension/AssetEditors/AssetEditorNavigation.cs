using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
internal static class AssetEditorNavigation {
    /// <summary>ファイルがソリューションのプロジェクトに含まれるか（「その他のファイル」は含めない）。</summary>
    internal static bool IsInSolution(IServiceProvider Services,string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Services.GetService(typeof(SVsUIShellOpenDocument)) is not IVsUIShellOpenDocument Documents){ return false; }
        if(ErrorHandler.Failed(Documents.IsDocumentInAProject(FilePath,out var Hierarchy,out var ItemId,out _,out var Found))){ return false; }
        return Found==(int)__VSDOCINPROJECT.DOCINPROJ_DocInProject&&Hierarchy is not null&&ItemId!=VSConstants.VSITEMID_NIL;
    }
    /// <summary>
    /// ソリューション エクスプローラーでファイルを選択する。ウィンドウを表示・作成せず（フォーカスを奪わない）、開いていない・
    /// プロジェクトにない場合は何もしないで false を返す。
    /// </summary>
    internal static bool SelectInSolution(IServiceProvider Services,string FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Services.GetService(typeof(SVsUIShellOpenDocument)) is not IVsUIShellOpenDocument Documents){ return false; }
        if(ErrorHandler.Failed(Documents.IsDocumentInAProject(FilePath,out var Hierarchy,out var ItemId,out _,out var Found))){ return false; }
        if(Found!=(int)__VSDOCINPROJECT.DOCINPROJ_DocInProject||Hierarchy is null||ItemId==VSConstants.VSITEMID_NIL){ return false; }
        if(Services.GetService(typeof(SVsUIShell)) is not IVsUIShell Shell){ return false; }
        var Explorer=VSConstants.StandardToolWindows.SolutionExplorer;
        if(ErrorHandler.Failed(Shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fFindFirst,ref Explorer,out var Frame))||Frame is null){ return false; }
        if(ErrorHandler.Failed(Frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView,out var View))||View is not IVsUIHierarchyWindow Window){ return false; }
        return ErrorHandler.Succeeded(Window.ExpandItem(Hierarchy,ItemId,EXPANDFLAGS.EXPF_ExpandParentsToShowItem))
            &&ErrorHandler.Succeeded(Window.ExpandItem(Hierarchy,ItemId,EXPANDFLAGS.EXPF_SelectItem));
    }
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
