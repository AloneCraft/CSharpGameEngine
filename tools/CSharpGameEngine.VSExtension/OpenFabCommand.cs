using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Extensions &gt; C# Game Engine メニュー/ツールバーのコマンド。既定のブラウザーで https://www.fab.com/ を開きます（ADR-0011）。ログイン、閲覧、購入、ダウンロードはすべてユーザー自身が管理するブラウザーセッションで行います。このコマンドは URL を起動するだけで、VSIX が一連の操作を自動化または傍受することはありません。
/// </summary>
internal sealed class OpenFabCommand {
    public const int CommandId=0x0103;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string FabUrl="https://www.fab.com/";

    private readonly AsyncPackage Package;

    private OpenFabCommand(AsyncPackage Package,OleMenuCommandService CommandService){
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

        _=new OpenFabCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        try {
            Process.Start(new ProcessStartInfo(FabUrl){ UseShellExecute=true });
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(OpenFabCommand),Ex.ToString());
            VsShellUtilities.ShowMessageBox(this.Package,
                VsixText.Format("OpenFab_BrowserLaunchFailed",Ex.Message),
                VsixText.Get("Common_ViewerTitle"),
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
