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
/// engine panel のレイアウトを再適用するのは、明示的なレイアウトコマンドからだけです。通常の起動ではシェルがレイアウトを復元し、AssetWorkspaceController は新しい Assets ペインだけを一度追加します。Assets は公開 linked-window API を使って Output に参加します。グループの形状はシェルが管理します。ドッキングされた DTE linked frame が IDE のメインフレームになる場合があるため、その Height を設定してはいけません。
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
    /// ツールバーを表示し、補助 Hierarchy ペインをキーボードフォーカスを奪わずに Solution Explorer の隣へドッキングします。Scene Designer は対象となる具体的な IScene が必要なため、ここでは扱わず、<see cref="OpenSceneDesignerCommand"/> からのみ開きます。
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
        var Assets=await Package.FindToolWindowAsync(typeof(AssetsToolWindowPane),0,true,Package.DisposalToken);
        if(Assets?.Frame is IVsWindowFrame AssetsFrame){
            DockAssets(AssetsFrame);
            ErrorHandler.ThrowOnFailure(AssetsFrame.ShowNoActivate());
        }
    }
    internal static void DockAssets(IVsWindowFrame Frame){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Output=new Guid(ToolWindowGuids80.Outputwindow);
        var Shell=Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell
            ??throw new InvalidOperationException("Visual Studio shell unavailable.");
        ErrorHandler.ThrowOnFailure(Shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate,ref Output,out var OutputFrame));
        ErrorHandler.ThrowOnFailure(OutputFrame.ShowNoActivate());
        ErrorHandler.ThrowOnFailure(Frame.GetProperty((int)__VSFPROPID.VSFPROPID_ExtWindowObject,out var WindowObject));
        ErrorHandler.ThrowOnFailure(OutputFrame.GetProperty((int)__VSFPROPID.VSFPROPID_ExtWindowObject,out var OutputObject));
        if(WindowObject is not EnvDTE.Window Window||OutputObject is not EnvDTE.Window OutputWindow){ throw new InvalidOperationException("Tool window automation is unavailable."); }
        var LinkedFrame=OutputWindow.LinkedWindowFrame;
        if(LinkedFrame is null){ LinkedFrame=OutputWindow.DTE.Windows.CreateLinkedWindowFrame(OutputWindow,Window,vsLinkedWindowType.vsLinkedWindowTypeDocked); }
        else if(!ReferenceEquals(Window.LinkedWindowFrame,LinkedFrame)){ LinkedFrame.LinkedWindows.Add(Window); }
        ErrorHandler.ThrowOnFailure(Frame.ShowNoActivate());
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
            // DTE.CommandBars の型は通常の System.Object です（強く型付けされた CommandBars
            // インターフェイスではありません）。EnvDTE80.DTE2 でも明示的な cast が必要です。
            if(Dte.CommandBars is not CommandBars CommandBars){
                return;
            }

            var Toolbar=CommandBars[ToolbarName];
            Toolbar.Visible=true;
        } catch(Exception){
            // ベストエフォートで実行します。ツールバーが見つからない、名前が変わった、または予期しない COM 例外が起きても、
            // package の初期化や ApplyGameEngineLayoutCommand を失敗させてはいけません。
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
