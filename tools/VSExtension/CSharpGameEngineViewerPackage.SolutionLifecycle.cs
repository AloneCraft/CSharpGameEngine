using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

public sealed partial class CSharpGameEngineViewerPackage:IVsSolutionEvents {
    private IVsSolution?SolutionEventsSource;
    private uint SolutionEventsCookie;

    private async Task InitializeSolutionLifecycleAsync(){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(
            this.DisposalToken);
        if(await this.GetServiceAsync(typeof(SVsSolution))
            is not IVsSolution Solution){
            throw new InvalidOperationException(
                "SVsSolution is unavailable for Scene lifecycle ownership.");
        }
        ErrorHandler.ThrowOnFailure(
            Solution.AdviseSolutionEvents(
                this,out this.SolutionEventsCookie));
        this.SolutionEventsSource=Solution;
        this.OnSolutionOpened();
    }

    /// <summary><see cref="CgeProjectContext"/> を開いているソリューションに設定し、プロジェクトに結び付いたサービスを再起動する (ADR-0065)。</summary>
    private void OnSolutionOpened(){
        ThreadHelper.ThrowIfNotOnUIThread();
        CgeProjectContext.SolutionPath=this.SolutionEventsSource is{ }Solution
            &&ErrorHandler.Succeeded(Solution.GetSolutionInfo(out _,out var SolutionFile,out _))
            &&!string.IsNullOrWhiteSpace(SolutionFile)?SolutionFile:null;
        this.AssetWorkspace?.SolutionChanged();
    }

    private void DisposeSolutionLifecycle(){
        ThreadHelper.ThrowIfNotOnUIThread();
        IVsSolution?Solution=this.SolutionEventsSource;
        uint Cookie=this.SolutionEventsCookie;
        this.SolutionEventsSource=null;
        this.SolutionEventsCookie=0;
        if(Solution is not null&&Cookie!=0){
            _=Solution.UnadviseSolutionEvents(Cookie);
        }
    }

    private void PrepareForSolutionClose(){
        ThreadHelper.ThrowIfNotOnUIThread();
        ActivityLog.LogInformation(
            nameof(CSharpGameEngineViewerPackage),
            "Preparing Scene subsystem for solution unload.");
        // シーンのドキュメントは C# の作成状態を投影したもの。プロジェクトの
        // アンロード開始前に破棄する。パッケージ破棄まで待つと、VisualStudioWorkspace と
        // Preview Host が残り、シェルがそれらを所有するプロジェクトをアンロードしようとしても終了しない。
        SceneJsonEditorPane.ReleaseRuntimeResourcesForSolutionClose();
        this.SceneSession?.ClearPin();
        ActivityLog.LogInformation(
            nameof(CSharpGameEngineViewerPackage),
            "Scene documents released before solution unload.");
    }

    int IVsSolutionEvents.OnBeforeCloseSolution(object Reserved){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.PrepareForSolutionClose();
        return VSConstants.S_OK;
    }

    int IVsSolutionEvents.OnAfterCloseSolution(object Reserved){
        ThreadHelper.ThrowIfNotOnUIThread();
        CgeProjectContext.SolutionPath=null;
        this.AssetWorkspace?.SolutionChanged();
        return VSConstants.S_OK;
    }
    int IVsSolutionEvents.OnAfterOpenProject(
        IVsHierarchy Hierarchy,int Added)=>VSConstants.S_OK;
    int IVsSolutionEvents.OnAfterOpenSolution(
        object Reserved,int NewSolution){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.OnSolutionOpened();
        return VSConstants.S_OK;
    }
    int IVsSolutionEvents.OnAfterLoadProject(
        IVsHierarchy StubHierarchy,IVsHierarchy RealHierarchy)=>
        VSConstants.S_OK;
    int IVsSolutionEvents.OnBeforeCloseProject(
        IVsHierarchy Hierarchy,int Removed)=>VSConstants.S_OK;
    int IVsSolutionEvents.OnBeforeUnloadProject(
        IVsHierarchy RealHierarchy,IVsHierarchy StubHierarchy)=>
        VSConstants.S_OK;
    int IVsSolutionEvents.OnQueryCloseProject(
        IVsHierarchy Hierarchy,int Removing,ref int Cancel)=>
        VSConstants.S_OK;
    int IVsSolutionEvents.OnQueryCloseSolution(
        object Reserved,ref int Cancel){
        ThreadHelper.ThrowIfNotOnUIThread();
        // OnBeforeCloseSolution は、シェルがプロジェクトのアンロードを開始した後にのみ呼び出される場合がある。
        // 問い合わせフェーズでこれらの派生ドキュメントを閉じると、その順序の循環を解消できる。
        this.PrepareForSolutionClose();
        return VSConstants.S_OK;
    }
    int IVsSolutionEvents.OnQueryUnloadProject(
        IVsHierarchy RealHierarchy,ref int Cancel)=>VSConstants.S_OK;
}
