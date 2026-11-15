using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
internal static class AssetEditorNavigation {
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
}
