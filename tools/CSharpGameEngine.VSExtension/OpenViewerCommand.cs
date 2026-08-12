using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that launches the existing, already-verified
/// <c>CSharpGameEngine.Extension</c> viewer as a separate top-level window (ADR-0006). Contains no
/// rendering/engine logic itself. See <see cref="ShowViewerToolWindowCommand"/>/
/// <see cref="ViewerHwndHost"/> (ADR-0007) for the embedded-in-VS alternative.
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
        MenuCommand MenuItem=new(Execute,MenuCommandId);
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
            ShowMessage(
                $"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。このVSIXは開発中のスケルトンであり、" +
                "ビルド時に埋め込んだパスでのみ動作します。詳細はADR-0006を参照してください。",
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
            ShowMessage($"ビューアの起動に失敗しました: {Ex.Message}",OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void ShowMessage(string Message,OLEMSGICON Icon){
        VsShellUtilities.ShowMessageBox(
            Package,
            Message,
            "C# Game Engine Viewer",
            Icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
