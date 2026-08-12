using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu/toolbar command that opens https://www.fab.com/ in the user's default browser
/// (ADR-0011). Login, browsing, purchasing, and downloading all happen entirely in that browser
/// session under the user's own control - this command does nothing beyond launching the URL, and
/// the VSIX never automates or intercepts any part of that flow.
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
        MenuCommand MenuItem=new(Execute,MenuCommandId);
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
            VsShellUtilities.ShowMessageBox(
                Package,
                $"ブラウザの起動に失敗しました: {Ex.Message}",
                "C# Game Engine Viewer",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
