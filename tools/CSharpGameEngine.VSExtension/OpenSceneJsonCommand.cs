using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0050: コンテキストメニューから<c>*.scene.json</c>を標準テキスト/JSONエディタで開く。
/// <see cref="OpenSceneDesignerCommand"/>と異なりRoslyn解析を一切必要としない —
/// <c>BeforeQueryStatus</c>はファイルパスの拡張子比較だけで完結する。
/// </summary>
internal sealed class OpenSceneJsonCommand {
    public const int CommandId=0x010B;

    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly ActiveDocumentTracker DocumentTracker;
    private readonly OleMenuCommand Command;

    private OpenSceneJsonCommand(OleMenuCommandService CommandService,ActiveDocumentTracker DocumentTracker){
        this.DocumentTracker=DocumentTracker??throw new ArgumentNullException(nameof(DocumentTracker));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        this.Command=new OleMenuCommand(this.Execute,new CommandID(CommandSet,CommandId));
        this.Command.BeforeQueryStatus+=this.UpdateStatus;
        CommandService.AddCommand(this.Command);
    }

    public static async Task InitializeAsync(AsyncPackage Package,ActiveDocumentTracker DocumentTracker){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service=await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new OpenSceneJsonCommand(CommandService,DocumentTracker);
    }

    private void UpdateStatus(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();
        bool IsSceneJson=this.ResolveTargetFilePath() is { } FilePath
            &&FilePath.EndsWith(".scene.json",StringComparison.OrdinalIgnoreCase);
        this.Command.Visible=IsSceneJson;
        this.Command.Enabled=IsSceneJson;
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.ResolveTargetFilePath() is not { } FilePath
            ||Package.GetGlobalService(typeof(SDTE))is not DTE Dte){
            return;
        }

        Dte.ItemOperations.OpenFile(FilePath);
    }

    /// <summary>ソリューションエクスプローラーでの右クリックを、アクティブなコードエディタ文書より優先する。</summary>
    private string?ResolveTargetFilePath(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.DocumentTracker.TryGetSolutionExplorerInvocationFilePath(out string?SolutionExplorerFilePath)
            ?SolutionExplorerFilePath
            :this.DocumentTracker.CurrentFilePath;
    }
}
