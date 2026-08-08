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
/// Thin VSIX shell: registers the viewer commands (<see cref="OpenViewerCommand"/>, ADR-0006 - a
/// separate top-level window launcher, distinct from the removed docked single-mesh preview), the
/// Fab drop-zone commands (<see cref="ToggleFabWatchCommand"/>, <see cref="OpenFabCommand"/>,
/// ADR-0011), the ECS-driven stage command (<see cref="OpenGameCommand"/>, ADR-0016), and the
/// Scene/Hierarchy/ShapePalette feature (<see cref="SceneFileEditorFactory"/>/<see cref="SceneWindowPane"/>,
/// <see cref="HierarchyToolWindowPane"/>/<see cref="ShowHierarchyToolWindowCommand"/>,
/// <see cref="ShapePaletteToolWindowPane"/>/<see cref="ShowShapePaletteToolWindowCommand"/>,
/// <see cref="ApplyGameEngineLayoutCommand"/>, ADR-0020/ADR-0021). Also auto-opens
/// <c>UserGameProject.slnx</c> when VS starts with no solution open, generates asset <c>.meta</c>
/// GUID sidecars, and registers the Solution Explorer 3D asset preview (ADR-0012/0013). Contains no
/// engine/rendering logic of its own.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
// Default docking (ADR-0021, Unity-style: Hierarchy left / ShapePalette right of Solution
// Explorer). Only takes effect the very first time each pane is created in a given hive - after
// that, VS's own persisted window layout (.winprf) wins, which is why
// ApplyGameEngineLayoutCommand/GameEngineLayout also exist as a runtime SetFramePos fallback.
[ProvideToolWindow(typeof(HierarchyToolWindowPane),
    Style = VsDockStyle.Linked, Window = ToolWindowGuids80.SolutionExplorer,
    Orientation = ToolWindowOrientation.Left, DockedWidth = 300)]
[ProvideToolWindow(typeof(ShapePaletteToolWindowPane),
    Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer)]
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
// Scene double-click preview (ADR-0020): registers SceneFileEditorFactory for *.scene. ADR-0021
// corrected this from the originally-shipped ".scene.json" - a compound two-dot extension that
// ProvideEditorExtension never actually matched, so the feature silently never opened anything.
[ProvideEditorFactory(typeof(SceneFileEditorFactory), 111, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorLogicalView(typeof(SceneFileEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorExtension(typeof(SceneFileEditorFactory), ".scene", 50)]
public sealed class CSharpGameEngineViewerPackage : AsyncPackage
{
    public const string PackageGuidString = "aec17804-e2e4-46e7-b373-de11af816203";

    private ToggleFabWatchCommand? _fabWatchCommand;
    private AssetMetaWatcher? _assetMetaWatcher;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await OpenViewerCommand.InitializeAsync(this);
        _fabWatchCommand = await ToggleFabWatchCommand.InitializeAsync(this);
        await OpenFabCommand.InitializeAsync(this);
        await OpenGameCommand.InitializeAsync(this);
        await ShowHierarchyToolWindowCommand.InitializeAsync(this);
        await ShowShapePaletteToolWindowCommand.InitializeAsync(this);
        await ApplyGameEngineLayoutCommand.InitializeAsync(this);
        await PreviewSceneDefinitionCommand.InitializeAsync(this);

        RegisterEditorFactory(new GlbFbxPreviewEditorFactory());
        RegisterEditorFactory(new SceneFileEditorFactory());

        _assetMetaWatcher = AssetMetaWatcher.StartIfProjectExists();

        await TryAutoOpenUserGameProjectAsync();

        // Deliberately NOT awaited here, and deliberately not called from inside
        // TryAutoOpenUserGameProjectAsync's own chain (it used to be - see ADR-0021 addendum
        // below). Creating a tool window for the first time (ShowToolWindowAsync ->
        // WindowManagerService.CreateContentPane) while still nested inside InitializeAsync's own
        // await chain, this early in the "no solution open" autoload sequence, deadlocked in a
        // real debug session: Parallel Stacks showed CreateContentPane's internal
        // JoinableTaskFactory.WaitSynchronouslyCore blocked waiting for AsyncPackage.FindToolWindowAsync,
        // which was itself blocked waiting for this same InitializeAsync call to return - the
        // shell apparently isn't ready for window-management calls this early, regardless of the
        // existing 2-second settle delay TryAutoOpenUserGameProjectAsync already uses for
        // solution/MainWindow state. Scheduling it as a fully independent top-level joinable task
        // lets InitializeAsync return and the shell continue settling before we touch tool windows.
        _ = JoinableTaskFactory.RunAsync(TryApplyInitialLayoutAsync);
    }

    /// <summary>
    /// Opens <c>UserGameProject.slnx</c> automatically (ADR-0012), but only when nothing is
    /// already open - this must never yank an already-open solution/project out from under a
    /// developer working on the engine itself (e.g. via CSharpGameEngine.slnx). Solution-opening
    /// only - does NOT touch tool windows (see the deadlock note on the <c>TryApplyInitialLayoutAsync</c>
    /// scheduling call in <see cref="InitializeAsync"/> for why that was deliberately split out).
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

    /// <summary>
    /// Applies the default GameEngine panel layout (ADR-0021) on a genuinely first launch for this
    /// hive, gated by <see cref="GameEngineLayout.WasInitialLayoutAppliedAsync"/> so later launches
    /// never fight the user's own subsequent layout changes - from then on VS's own "restore what
    /// was open last time" behavior is what's relied on instead.
    ///
    /// Deliberately its own top-level entry point (scheduled via <c>JoinableTaskFactory.RunAsync</c>
    /// from <see cref="InitializeAsync"/>, not awaited inline there or chained inside
    /// <see cref="TryAutoOpenUserGameProjectAsync"/>) - see the deadlock note at the call site.
    /// </summary>
    private async Task TryApplyInitialLayoutAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

        try
        {
            if (await GameEngineLayout.WasInitialLayoutAppliedAsync(this))
            {
                return;
            }

            string? repoRoot = RepoLocator.ResolveRepoRoot();
            if (repoRoot is null)
            {
                return;
            }

            if (await GetServiceAsync(typeof(SDTE)) is not DTE dte)
            {
                return;
            }

            // Give the shell substantially more time to finish its own startup than the 2s used by
            // TryAutoOpenUserGameProjectAsync for solution/MainWindow state - tool window creation
            // apparently needs more of the shell ready than "MainWindow exists" (empirically, the
            // deadlock this works around still happened after that 2s delay, chained on the same
            // call stack; a longer delay alone was not tested in isolation since the call stack
            // split above is the primary fix - kept as an extra margin, not the sole mitigation).
            await Task.Delay(TimeSpan.FromSeconds(3));

            await GameEngineLayout.ApplyAsync(this, dte, repoRoot);
            await GameEngineLayout.MarkInitialLayoutAppliedAsync(this);
        }
        catch (COMException)
        {
            // Never let a failure here take down VS startup or leave InitialLayoutApplied stuck
            // unset in a way that would just fail identically on every future launch.
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
