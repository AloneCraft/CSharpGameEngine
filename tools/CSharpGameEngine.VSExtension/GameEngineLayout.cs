using System;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Builds the "GameEngine" default panel arrangement (ADR-0021): the "C# Game Engine" toolbar made
/// visible, Hierarchy docked left / ShapePalette docked right of Solution Explorer, and the default
/// scene opened as a document tab. Two call sites: once automatically on a genuinely first launch
/// (<see cref="CSharpGameEngineViewerPackage"/>, gated by <see cref="WasInitialLayoutAppliedAsync"/>
/// so it never re-fights a user's own later layout changes - VS's own window-profile persistence
/// handles "restore what was open last time" from then on), and on demand via
/// <see cref="ApplyGameEngineLayoutCommand"/>.
///
/// No VS SDK API registers a named "Window &gt; Apply Window Layout" entry (that feature's real
/// implementation, <c>ILayoutManagementServiceInternal</c>, lives in a non-SDK, internal VS
/// assembly) - <see cref="IVsWindowFrame.SetFramePos"/> is the one publicly supported way to force
/// tool-window placement, and is used here instead.
/// </summary>
internal static class GameEngineLayout
{
    private const string SettingsCollectionPath = "CSharpGameEngine";
    private const string InitialLayoutAppliedPropertyName = "InitialLayoutApplied";
    private const string ToolbarName = "C# Game Engine";
    private const string DefaultSceneRelativePath = @"UserGameProject\DefaultScene.scene";

    public static async Task<bool> WasInitialLayoutAppliedAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        WritableSettingsStore store = GetUserSettingsStore(package);
        return store.CollectionExists(SettingsCollectionPath)
            && store.PropertyExists(SettingsCollectionPath, InitialLayoutAppliedPropertyName)
            && store.GetBoolean(SettingsCollectionPath, InitialLayoutAppliedPropertyName);
    }

    public static async Task MarkInitialLayoutAppliedAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        WritableSettingsStore store = GetUserSettingsStore(package);
        if (!store.CollectionExists(SettingsCollectionPath))
        {
            store.CreateCollection(SettingsCollectionPath);
        }

        store.SetBoolean(SettingsCollectionPath, InitialLayoutAppliedPropertyName, true);
    }

    private static WritableSettingsStore GetUserSettingsStore(AsyncPackage package)
    {
        ShellSettingsManager settingsManager = new(package);
        return settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
    }

    /// <summary>Shows the toolbar, docks Hierarchy left / ShapePalette right of Solution Explorer, and opens the default scene as a document tab.</summary>
    public static async Task ApplyAsync(AsyncPackage package, DTE dte, string repoRoot)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        ShowToolbar(dte);

        ToolWindowPane hierarchyWindow = await package.ShowToolWindowAsync(typeof(HierarchyToolWindowPane), 0, true, package.DisposalToken);
        ToolWindowPane shapeWindow = await package.ShowToolWindowAsync(typeof(ShapePaletteToolWindowPane), 0, true, package.DisposalToken);

        Guid solutionExplorer = new(ToolWindowGuids80.SolutionExplorer);
        SetFramePos(hierarchyWindow, VSSETFRAMEPOS.SFP_fDock | VSSETFRAMEPOS.SFP_fDockLeft, solutionExplorer, width: 300);
        SetFramePos(shapeWindow, VSSETFRAMEPOS.SFP_fDock | VSSETFRAMEPOS.SFP_fDockRight, solutionExplorer, width: 260);

        string scenePath = Path.Combine(repoRoot, DefaultSceneRelativePath);
        if (File.Exists(scenePath))
        {
            // dte.ItemOperations.OpenFile(...) is the confirmed-reliable way to route through the
            // real IVsEditorFactory-based open path (ADR-0013) - mere selection or a raw file-open
            // API does not reliably trigger a custom editor factory.
            dte.ItemOperations.OpenFile(scenePath);
        }
    }

    private static void ShowToolbar(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            // DTE.CommandBars is typed as plain System.Object (not the strongly-typed CommandBars
            // interface) even on EnvDTE80.DTE2 - an explicit cast is required.
            if (dte.CommandBars is not CommandBars commandBars)
            {
                return;
            }

            CommandBar toolbar = commandBars[ToolbarName];
            toolbar.Visible = true;
        }
        catch (Exception)
        {
            // Best-effort: a missing/renamed toolbar or unexpected COM failure must never take
            // down package init or ApplyGameEngineLayoutCommand.
        }
    }

    private static void SetFramePos(ToolWindowPane pane, VSSETFRAMEPOS position, Guid relativeTo, int width)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (pane.Frame is not IVsWindowFrame frame)
        {
            return;
        }

        frame.SetFramePos(position, ref relativeTo, 0, 0, width, 0);
    }
}
