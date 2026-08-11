using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX shell for the engine commands, asset preview, and the ECS project auto-open flow.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly=true,AllowsBackgroundLoading=true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu",1)]
[ProvideToolWindow(typeof(HierarchyToolWindowPane),
    Style=VsDockStyle.Linked,Window=ToolWindowGuids80.SolutionExplorer,
    Orientation=ToolWindowOrientation.Left,DockedWidth=300)]
[ProvideToolWindow(typeof(ShapePaletteToolWindowPane),
    Style=VsDockStyle.Tabbed,Window=ToolWindowGuids80.SolutionExplorer)]
[ProvideAutoLoad("adfc4e64-0397-11d1-9f4e-00a0c911004f",PackageAutoLoadFlags.BackgroundLoad)]
[ProvideEditorFactory(typeof(GlbFbxPreviewEditorFactory),110,TrustLevel=__VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(GlbFbxPreviewEditorFactory),VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".glb",50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".gltf",50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory),".fbx",50)]
[ProvideEditorFactory(typeof(SceneFileEditorFactory),111,TrustLevel=__VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(SceneFileEditorFactory),VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(SceneFileEditorFactory),".scene",50)]
public sealed class CSharpGameEngineViewerPackage:AsyncPackage {
    public const string PackageGuidString="aec17804-e2e4-46e7-b373-de11af816203";

    private ToggleFabWatchCommand?FabWatchCommand;
    private AssetMetaWatcher?AssetMetaWatcher;

    protected override async Task InitializeAsync(CancellationToken cancellationToken,IProgress<ServiceProgressData>progress){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await OpenViewerCommand.InitializeAsync(this);
        FabWatchCommand= await ToggleFabWatchCommand.InitializeAsync(this);
        await OpenFabCommand.InitializeAsync(this);
        await OpenGameCommand.InitializeAsync(this);
        await ShowHierarchyToolWindowCommand.InitializeAsync(this);
        await ShowShapePaletteToolWindowCommand.InitializeAsync(this);
        await ApplyGameEngineLayoutCommand.InitializeAsync(this);
        await PreviewSceneDefinitionCommand.InitializeAsync(this);

        RegisterEditorFactory(new GlbFbxPreviewEditorFactory());
        RegisterEditorFactory(new SceneFileEditorFactory());

        AssetMetaWatcher=AssetMetaWatcher.StartIfProjectExists();
        await TryAutoOpenUserEcsProjectAsync();

        _=JoinableTaskFactory.RunAsync(TryApplyInitialLayoutAsync);
    }

    /// <summary>
    /// Opens UserEcsProject.slnx on a fresh VS start when no solution is already open.
    /// Existing solutions are never replaced.
    /// </summary>
    private async Task TryAutoOpenUserEcsProjectAsync(){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();

        if(await GetServiceAsync(typeof(SVsSolution))is not IVsSolution solution){
            return;
        }

        solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen,out var IsOpenObj);
        if(IsOpenObj is bool IsOpen&&IsOpen){
            return;
        }

        var RepoRoot=RepoLocator.ResolveRepoRoot();
        if(RepoRoot is null){
            return;
        }

        var SolutionPath=Path.Combine(RepoRoot,"UserEcsProject.slnx");
        if(!File.Exists(SolutionPath)|| await GetServiceAsync(typeof(SDTE))is not DTE Dte){
            return;
        }

        try {
            await Task.Delay(TimeSpan.FromSeconds(2));
            Dte.ExecuteCommand("File.OpenProject", $"\"{SolutionPath}\"");
            Dte.MainWindow.Visible=true;
            Dte.MainWindow.Activate();
        } catch(COMException){
            // VS startup must remain usable even if the shell rejects the early open request.
        }
    }

    private async Task TryApplyInitialLayoutAsync(){
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

        try {
            if(await GameEngineLayout.WasInitialLayoutAppliedAsync(this)){
                return;
            }

            var RepoRoot=RepoLocator.ResolveRepoRoot();
            if(RepoRoot is null|| await GetServiceAsync(typeof(SDTE))is not DTE Dte){
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
            await GameEngineLayout.ApplyAsync(this,Dte,RepoRoot);
            await GameEngineLayout.MarkInitialLayoutAppliedAsync(this);
        } catch(COMException){
            // Layout is optional and must not prevent VS from starting.
        }
    }

    protected override void Dispose(bool Disposing){
        if(Disposing){
            FabWatchCommand?.Dispose();
            AssetMetaWatcher?.Dispose();
        }

        base.Dispose(Disposing);
    }
}
