using System;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Builds the "GameEngine" default panel arrangement (ADR-0021): the "C# Game Engine" toolbar made
/// visible and Hierarchy docked left of Solution Explorer without activating the pane. The Shape
/// Palette that used to dock right was removed in M0020 Phase 0 along with the rest of the dead
/// <c>.scene</c> write path. Two call sites: once automatically on a genuinely first launch
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

    public static async Task<bool>WasInitialLayoutAppliedAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Store=GetUserSettingsStore(Package);
        return Store.CollectionExists(SettingsCollectionPath)
            &&Store.PropertyExists(SettingsCollectionPath,InitialLayoutAppliedPropertyName)
            &&Store.GetBoolean(SettingsCollectionPath,InitialLayoutAppliedPropertyName);
    }

    public static async Task MarkInitialLayoutAppliedAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Store=GetUserSettingsStore(Package);
        if(!Store.CollectionExists(SettingsCollectionPath)){
            Store.CreateCollection(SettingsCollectionPath);
        }

        Store.SetBoolean(SettingsCollectionPath,InitialLayoutAppliedPropertyName,true);
    }

    private static WritableSettingsStore GetUserSettingsStore(AsyncPackage Package){
        ShellSettingsManager SettingsManager=new(Package);
        return SettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
    }

    /// <summary>
    /// Shows the toolbar and docks the auxiliary Hierarchy pane beside Solution Explorer without
    /// taking keyboard focus. Scene Designer is deliberately excluded: it needs a concrete IScene
    /// target and is opened only through <see cref="OpenSceneDesignerCommand"/>.
    /// </summary>
    public static async Task ApplyAsync(AsyncPackage Package,DTE Dte){
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);

        ShowToolbar(Dte);

        Guid SolutionExplorer=new(ToolWindowGuids80.SolutionExplorer);
        foreach(var Specification in InitialLayoutToolWindowPlan.ToolWindows){
            var Pane=await Package.FindToolWindowAsync(
                                                       GetToolWindowType(Specification.Kind),
                                                       0,
                                                       true,
                                                       Package.DisposalToken);
            SetFramePos(Pane,GetFramePosition(Specification.Kind),SolutionExplorer,Specification.Width);

            if(Specification.Presentation==ToolWindowPresentation.NonActivating){
                ShowFrameWithoutActivation(Pane);
            }
        }
    }

    private static System.Type GetToolWindowType(InitialLayoutToolWindowKind Kind)=>Kind switch {
        InitialLayoutToolWindowKind.Hierarchy=>typeof(HierarchyToolWindowPane),
        InitialLayoutToolWindowKind.Inspector=>typeof(InspectorToolWindowPane),
        InitialLayoutToolWindowKind.ComponentPalette=>typeof(ComponentPaletteToolWindowPane),
        _=>throw new System.ArgumentOutOfRangeException(nameof(Kind),Kind,VsixText.Get("GameEngineLayout_InvalidToolWindow")),
    };

    private static VSSETFRAMEPOS GetFramePosition(InitialLayoutToolWindowKind Kind)=>Kind switch {
        InitialLayoutToolWindowKind.Hierarchy=>VSSETFRAMEPOS.SFP_fDock|VSSETFRAMEPOS.SFP_fDockLeft,
        InitialLayoutToolWindowKind.Inspector=>VSSETFRAMEPOS.SFP_fDock|VSSETFRAMEPOS.SFP_fDockRight,
        // 右側の下段。Inspectorとタブ結合させないのは、ドラッグ中に隠れるドロップ先が成立しないため。
        InitialLayoutToolWindowKind.ComponentPalette=>VSSETFRAMEPOS.SFP_fDock|VSSETFRAMEPOS.SFP_fDockBottom,
        _=>throw new System.ArgumentOutOfRangeException(nameof(Kind),Kind,VsixText.Get("GameEngineLayout_InvalidToolWindow")),
    };

    private static void ShowFrameWithoutActivation(ToolWindowPane Pane){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Pane.Frame is not IVsWindowFrame Frame){
            return;
        }

        ToolWindowFramePresenter.ShowWithoutActivation(
            ()=>ShowNoActivate(Frame),
            RestoreFocus:null);
    }

    private static void ShowNoActivate(IVsWindowFrame Frame){
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(Frame.ShowNoActivate());
    }

    private static void ShowToolbar(DTE Dte){
        ThreadHelper.ThrowIfNotOnUIThread();

        try {
            // DTE.CommandBars is typed as plain System.Object (not the strongly-typed CommandBars
            // interface) even on EnvDTE80.DTE2 - an explicit cast is required.
            if(Dte.CommandBars is not CommandBars CommandBars){
                return;
            }

            var Toolbar=CommandBars[ToolbarName];
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
