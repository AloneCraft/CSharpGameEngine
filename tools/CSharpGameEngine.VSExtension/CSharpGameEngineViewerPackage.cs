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
/// Thin VSIX shell: registers the viewer commands (<see cref="OpenViewerCommand"/> ADR-0006,
/// <see cref="ShowViewerToolWindowCommand"/>/<see cref="ViewerToolWindowPane"/> ADR-0007/0008/0010)
/// and the Fab drop-zone commands (<see cref="ToggleFabWatchCommand"/>,
/// <see cref="OpenFabCommand"/>, ADR-0011). Also auto-opens <c>UserGameProject.slnx</c> when VS
/// starts with no solution open, generates asset <c>.meta</c> GUID sidecars, and registers the
/// Solution Explorer 3D asset preview (ADR-0012/0013). Contains no engine/rendering logic of its
/// own.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(ViewerToolWindowPane))]
// Loads this package automatically when VS reaches a "no solution open" state (in addition to the
// existing lazy command-triggered load path) so UserGameProject.slnx can be auto-opened for a
// genuinely fresh VS launch, without affecting the normal engine-development flow of opening
// CSharpGameEngine.slnx (which already has a solution open, so TryAutoOpenUserGameProjectAsync's
// own defensive IsSolutionOpen check below is a no-op in that case regardless of load path).
// VSConstants.UICONTEXT_NoSolution is typed as System.Guid, not string, so it can't be used
// directly as an attribute argument (attributes require compile-time constants). Confirmed its
// actual value via reflection against the referenced Microsoft.VisualStudio.Shell.Framework.dll
// (17.14.40264) rather than guessing from memory - it's "adfc4e64-0397-11d1-9f4e-00a0c911004f".
[ProvideAutoLoad("adfc4e64-0397-11d1-9f4e-00a0c911004f", PackageAutoLoadFlags.BackgroundLoad)]
// Solution Explorer 3D asset preview (ADR-0013): registers GlbFbxPreviewEditorFactory for
// .glb/.gltf/.fbx. No ProjectGuid scoping, so it applies generically regardless of project type.
[ProvideEditorFactory(typeof(GlbFbxPreviewEditorFactory), 110, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(GlbFbxPreviewEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory), ".glb", 50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory), ".gltf", 50)]
[ProvideEditorExtension(typeof(GlbFbxPreviewEditorFactory), ".fbx", 50)]
public sealed class CSharpGameEngineViewerPackage : AsyncPackage
{
    public const string PackageGuidString = "aec17804-e2e4-46e7-b373-de11af816203";

    private ToggleFabWatchCommand? _fabWatchCommand;
    private AssetMetaWatcher? _assetMetaWatcher;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await OpenViewerCommand.InitializeAsync(this);
        await ShowViewerToolWindowCommand.InitializeAsync(this);
        _fabWatchCommand = await ToggleFabWatchCommand.InitializeAsync(this);
        await OpenFabCommand.InitializeAsync(this);

        RegisterEditorFactory(new GlbFbxPreviewEditorFactory());

        _assetMetaWatcher = AssetMetaWatcher.StartIfProjectExists();

        await TryAutoOpenUserGameProjectAsync();
    }

    /// <summary>
    /// Opens <c>UserGameProject.slnx</c> automatically (ADR-0012), but only when nothing is
    /// already open - this must never yank an already-open solution/project out from under a
    /// developer working on the engine itself (e.g. via CSharpGameEngine.slnx).
    /// </summary>
    private async Task TryAutoOpenUserGameProjectAsync()
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (await GetServiceAsync(typeof(SVsSolution)) is not IVsSolution solution)
        {
            return;
        }

        solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen, out object isOpenObj);
        if (isOpenObj is bool isOpen && isOpen)
        {
            return;
        }

        string? repoRoot = RepoLocator.ResolveRepoRoot();
        if (repoRoot is null)
        {
            return;
        }

        string solutionPath = Path.Combine(repoRoot, "UserGameProject.slnx");
        if (!File.Exists(solutionPath))
        {
            return;
        }

        if (await GetServiceAsync(typeof(SDTE)) is not DTE dte)
        {
            return;
        }

        try
        {
            // This autoload path runs very early - at the exact moment the "no solution open" UI
            // context first becomes active, which races with the shell's own Start Window
            // construction as part of its normal startup sequence. A short delay lets the shell's
            // own startup settle first before we touch solution/window state.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // dte.Solution.Open(solutionPath) was tried first and found (empirically, via a real Exp
            // instance launch) to only half-work: it registered the solution in the MRU list but left
            // the Start Window showing instead of actually switching to the opened solution. Routing
            // through the same "File.OpenProject" command a user's menu click would invoke is more
            // robust - it exercises the real, well-tested open-project code path instead of the raw
            // Solution.Open API entry point.
            dte.ExecuteCommand("File.OpenProject", $"\"{solutionPath}\"");

            // Confirmed empirically: the project genuinely loads correctly at this point
            // (Solution.IsOpen, Projects.Count=1, Solution Explorer/Git Changes tool windows both
            // present) but the shell's own MainWindow is still marked not-visible this early in the
            // "no solution" autoload sequence, so nothing shows on screen despite the backend state
            // being correct. Explicitly showing/activating it closes that gap.
            dte.MainWindow.Visible = true;
            dte.MainWindow.Activate();
        }
        catch (COMException)
        {
            // Never let a failure here take down VS startup - worst case, the user just doesn't
            // get the auto-opened solution and can open UserGameProject.slnx manually.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fabWatchCommand?.Dispose();
            _assetMetaWatcher?.Dispose();
        }

        base.Dispose(disposing);
    }
}
