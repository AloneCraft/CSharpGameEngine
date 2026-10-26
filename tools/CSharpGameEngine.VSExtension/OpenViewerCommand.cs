using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 既存の、検証済みの <c>CSharpGameEngine.Extension</c> viewer を独立したトップレベルウィンドウとして起動する Extensions &gt; C# Game Engine メニューコマンド（ADR-0006）。このクラス自体はレンダリングやエンジンのロジックを含みません。Visual Studio 内に埋め込む代替方式については <see cref="ShowViewerToolWindowCommand"/>/<see cref="ViewerHwndHost"/>（ADR-0007）を参照してください。
/// </summary>
internal sealed class OpenViewerCommand {
    public const int CommandId=0x0100;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string ViewerModelArgument="Samples/Box.glb";

    private readonly AsyncPackage Package;

    private OpenViewerCommand(AsyncPackage Package,OleMenuCommandService CommandService){
        this.Package= Package ?? throw new ArgumentNullException(nameof(Package));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        MenuCommand MenuItem=new(this.Execute,MenuCommandId);
        CommandService.AddCommand(MenuItem);
    }

    public static async Task InitializeAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service= await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new OpenViewerCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        var RepoRoot=RepoLocator.ResolveRepoRoot();
        if(RepoRoot is null){
            this.ShowMessage(
                             VsixText.Format("OpenViewer_RepoRootNotFound",RepoLocator.RepoMarkerFileName),
                             OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        ProcessStartInfo StartInfo=new("dotnet", $"run --project tools/CSharpGameEngine.Extension -- --model {ViewerModelArgument}"){
            WorkingDirectory=RepoRoot,
            UseShellExecute=false,
        };

        try {
            Process.Start(StartInfo);
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(OpenViewerCommand),Ex.ToString());
            this.ShowMessage(VsixText.Format("OpenViewer_LaunchFailed",Ex.Message),OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void ShowMessage(string Message,OLEMSGICON Icon){
        VsShellUtilities.ShowMessageBox(this.Package,
            Message,
            VsixText.Get("Common_ViewerTitle"),
            Icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
