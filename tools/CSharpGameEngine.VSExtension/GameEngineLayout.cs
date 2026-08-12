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
internal static class GameEngineLayout {
    private const string SettingsCollectionPath="CSharpGameEngine";
    private const string InitialLayoutAppliedPropertyName="InitialLayoutApplied";
    private const string ToolbarName="C# Game Engine";

    public static async Task<bool>WasInitialLayoutAppliedAsync(AsyncPackage package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        WritableSettingsStore Store=GetUserSettingsStore(package);
        return Store.CollectionExists(SettingsCollectionPath)
            &&Store.PropertyExists(SettingsCollectionPath,InitialLayoutAppliedPropertyName)
            &&Store.GetBoolean(SettingsCollectionPath,InitialLayoutAppliedPropertyName);
    }

    public static async Task MarkInitialLayoutAppliedAsync(AsyncPackage package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        WritableSettingsStore Store=GetUserSettingsStore(package);
        if(!Store.CollectionExists(SettingsCollectionPath)){
            Store.CreateCollection(SettingsCollectionPath);
        }

        Store.SetBoolean(SettingsCollectionPath,InitialLayoutAppliedPropertyName,true);
    }

    private static WritableSettingsStore GetUserSettingsStore(AsyncPackage package){
        ShellSettingsManager settingsManager=new(package);
        return settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
    }

    /// <summary>
    /// Shows the toolbar and docks Hierarchy / Scene Designer / ShapePalette around Solution
    /// Explorer. No longer opens a default scene document tab (ADR-0040 Phase 9 retired the
    /// <c>.scene</c> document editor entirely - Scene Designer is opened per-class via
    /// <see cref="OpenSceneDesignerCommand"/>'s code-editor context menu entry instead of a fixed
    /// default file).
    /// </summary>
    public static async Task ApplyAsync(AsyncPackage package,DTE Dte,string RepoRoot){
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        ShowToolbar(Dte);

        ToolWindowPane HierarchyWindow= await package.ShowToolWindowAsync(typeof(HierarchyToolWindowPane),0,true,package.DisposalToken);
        ToolWindowPane SceneDesignerWindow= await package.ShowToolWindowAsync(typeof(SceneDesignerToolWindowPane),0,true,package.DisposalToken);
        ToolWindowPane ShapeWindow= await package.ShowToolWindowAsync(typeof(ShapePaletteToolWindowPane),0,true,package.DisposalToken);

        Guid SolutionExplorer=new(ToolWindowGuids80.SolutionExplorer);
        SetFramePos(HierarchyWindow,VSSETFRAMEPOS.SFP_fDock|VSSETFRAMEPOS.SFP_fDockLeft,SolutionExplorer,Width:300);
        SetFramePos(SceneDesignerWindow,VSSETFRAMEPOS.SFP_fDock|VSSETFRAMEPOS.SFP_fDockRight,SolutionExplorer,Width:260);
        SetFramePos(ShapeWindow,VSSETFRAMEPOS.SFP_fDock|VSSETFRAMEPOS.SFP_fDockRight,SolutionExplorer,Width:260);
    }

    private static void ShowToolbar(DTE Dte){
        ThreadHelper.ThrowIfNotOnUIThread();

        try {
            // DTE.CommandBars is typed as plain System.Object (not the strongly-typed CommandBars
            // interface) even on EnvDTE80.DTE2 - an explicit cast is required.
            if(Dte.CommandBars is not CommandBars commandBars){
                return;
            }

            CommandBar Toolbar=commandBars[ToolbarName];
            Toolbar.Visible=true;
        } catch(Exception){
            // Best-effort: a missing/renamed toolbar or unexpected COM failure must never take
            // down package init or ApplyGameEngineLayoutCommand.
        }
    }

    private static void SetFramePos(ToolWindowPane Pane,VSSETFRAMEPOS Position,Guid RelativeTo,int Width){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Pane.Frame is not IVsWindowFrame Frame){
            return;
        }

        Frame.SetFramePos(Position,ref RelativeTo,0,0,Width,0);
    }
}
