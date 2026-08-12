using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Code-editor context-menu command (<c>IDM_VS_CTXT_CODEWIN</c>) that opens the Scene Designer tool
/// window targeted at an <c>IScene</c> class in the active <c>.cs</c> file (ADR-0040 Phase 9).
/// Reuses <see cref="SceneWorkspaceService"/>'s already-debounced analysis (via
/// <see cref="SceneEditorHub.ActiveAllSceneModels"/>) rather than running Roslyn itself - both
/// <see cref="UpdateVisibility"/> and <see cref="Execute"/> just read the cached result.
/// </summary>
internal sealed class OpenSceneDesignerCommand {
    public const int CommandId=0x0109;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly OleMenuCommand Command;

    private OpenSceneDesignerCommand(AsyncPackage package,OleMenuCommandService CommandService){
        Package=package??throw new ArgumentNullException(nameof(package));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        Command=new OleMenuCommand(Execute,MenuCommandId);
        Command.BeforeQueryStatus+=UpdateVisibility;
        CommandService.AddCommand(Command);
    }

    public static async Task InitializeAsync(AsyncPackage package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var Service= await package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new OpenSceneDesignerCommand(package,CommandService);
    }

    private void UpdateVisibility(object Sender,EventArgs E){
        Command.Visible=SceneEditorHub.ActiveAllSceneModels.Count>0;
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        IReadOnlyList<SceneModel>Models=SceneEditorHub.ActiveAllSceneModels;
        if(Models.Count==0){
            SetStatusBarText("このファイルには IScene 実装クラスが見つかりません。");
            return;
        }

        SceneModel?Chosen=Models.Count==1?Models[0]:PickScene(Models);
        if(Chosen is null){
            return;
        }

        var FilePath=Chosen.FilePath;
        var ClassName=Chosen.ClassName;

        _=Package.JoinableTaskFactory.RunAsync(async delegate {
            ToolWindowPane Window= await Package.ShowToolWindowAsync(typeof(SceneDesignerToolWindowPane),0,true,Package.DisposalToken);
            if(Window is not SceneDesignerToolWindowPane Pane||Pane.Frame is null){
                throw new NotSupportedException("Cannot create the Scene Designer tool window.");
            }

            Pane.SetTarget(FilePath,ClassName);
        });
    }

    private SceneModel?PickScene(IReadOnlyList<SceneModel>Models){
        ThreadHelper.ThrowIfNotOnUIThread();

        ScenePickerDialog Dialog=new([.. Models.Select(M=>M.ClassName)]);
        return Dialog.ShowDialog()==true&&Dialog.SelectedIndex is { } Index
            ?Models[Index]
            :null;
    }

    private void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();

        // Package here resolves to the instance field, not the Microsoft.VisualStudio.Shell.Package
        // type - GetGlobalService is static, so it needs the fully-qualified type name.
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }
}
