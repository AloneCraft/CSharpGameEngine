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
/// Captures the active C# path and asks <see cref="SceneWorkspaceService"/> to analyse that exact
/// document asynchronously. It does not consume the debounced global cache for its 0/1/N decision,
/// so focus changes cannot swap in another document between query-status and execution.
/// </summary>
internal sealed class OpenSceneDesignerCommand {
    public const int CommandId=0x0109;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly SceneWorkspaceService Workspace;
    private readonly ActiveDocumentTracker DocumentTracker;
    private readonly OleMenuCommand Command;

    private OpenSceneDesignerCommand(
        AsyncPackage Package,
        OleMenuCommandService CommandService,
        SceneWorkspaceService Workspace,
        ActiveDocumentTracker DocumentTracker){
        this.Package= Package ?? throw new ArgumentNullException(nameof(Package));
        this.Workspace=Workspace??throw new ArgumentNullException(nameof(Workspace));
        this.DocumentTracker=DocumentTracker??throw new ArgumentNullException(nameof(DocumentTracker));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        Command=new OleMenuCommand(Execute,MenuCommandId);
        Command.BeforeQueryStatus+=UpdateVisibility;
        CommandService.AddCommand(Command);
    }

    public static async Task InitializeAsync(
        AsyncPackage Package,
        SceneWorkspaceService Workspace,
        ActiveDocumentTracker DocumentTracker){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service= await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new OpenSceneDesignerCommand(Package,CommandService,Workspace,DocumentTracker);
    }

    private void UpdateVisibility(object Sender,EventArgs E){
        Command.Visible=this.DocumentTracker.CurrentFilePath is { } FilePath
            &&FilePath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase);
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        string?FilePath=this.DocumentTracker.CurrentFilePath;
        if(FilePath is null){
            SetStatusBarText("アクティブな C# ファイルを特定できませんでした。");
            return;
        }

        SetStatusBarText("シーンを解析しています...");

        _=Package.JoinableTaskFactory.RunAsync(async delegate {
            try {
                IReadOnlyList<SceneModel>Models=await this.Workspace.AnalyzeFileNowAsync(
                    FilePath,
                    Package.DisposalToken).ConfigureAwait(false);
                await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);

                if(Models.Count==0){
                    SetStatusBarText("このファイルには IScene 実装クラスが見つかりません。");
                    return;
                }

                SceneModel?Chosen=Models.Count==1?Models[0]:PickScene(Models);
                if(Chosen is null){
                    return;
                }

                ToolWindowPane Window=await Package.ShowToolWindowAsync(
                    typeof(SceneDesignerToolWindowPane),
                    0,
                    true,
                    Package.DisposalToken);
                if(Window is not SceneDesignerToolWindowPane Pane||Pane.Frame is null){
                    throw new NotSupportedException("Cannot create the Scene Designer tool window.");
                }

                Pane.SetTarget(Chosen);
                SetStatusBarText("シーン デザイナーを開きました。");
            } catch(OperationCanceledException) when (Package.DisposalToken.IsCancellationRequested){
                // Package shutdown.
            } catch(Exception Ex){
                await Package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.LogError(nameof(OpenSceneDesignerCommand),Ex.ToString());
                SetStatusBarText("シーンの解析またはデザイナー表示に失敗しました。詳細は ActivityLog.xml を確認してください。");
            }
        });
    }

    private SceneModel?PickScene(IReadOnlyList<SceneModel>Models){
        ThreadHelper.ThrowIfNotOnUIThread();

        ScenePickerDialog Dialog=new([.. Models.Select(M=>M.ClassName)]);
        return Dialog.ShowModal()==true&&Dialog.SelectedIndex is { } Index
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
