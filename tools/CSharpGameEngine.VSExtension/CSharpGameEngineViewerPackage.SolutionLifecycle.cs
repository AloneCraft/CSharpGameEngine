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
        // Scene documents are projections of C# authoring state. Release them before project
        // unload starts; waiting until package disposal can leave VisualStudioWorkspace and a
        // Preview Host alive while the shell is trying to unload their owning project.
        this.SceneDesignerDocuments?.ReleaseRuntimeResourcesForSolutionClose();
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

    int IVsSolutionEvents.OnAfterCloseSolution(object Reserved)=>
        VSConstants.S_OK;
    int IVsSolutionEvents.OnAfterOpenProject(
        IVsHierarchy Hierarchy,int Added)=>VSConstants.S_OK;
    int IVsSolutionEvents.OnAfterOpenSolution(
        object Reserved,int NewSolution)=>VSConstants.S_OK;
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
        // OnBeforeCloseSolution may be raised only after the shell has begun unloading projects.
        // Closing these derived documents during the query phase removes that ordering cycle.
        this.PrepareForSolutionClose();
        return VSConstants.S_OK;
    }
    int IVsSolutionEvents.OnQueryUnloadProject(
        IVsHierarchy RealHierarchy,ref int Cancel)=>VSConstants.S_OK;
}
