using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that launches UserEcsProject as a separate top-level window.
/// The VSIX remains a .NET Framework process, so the net10.0 ECS application is intentionally
/// started out of process rather than referenced or executed in-process.
/// </summary>
internal sealed class OpenGameCommand {
    public const int CommandId=0x0104;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string ProjectPath="UserEcsProject";

    private readonly AsyncPackage Package;

    private OpenGameCommand(AsyncPackage Package,OleMenuCommandService CommandService){
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

        _=new OpenGameCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        var RepoRoot=RepoLocator.ResolveRepoRoot();
        if(RepoRoot is null){
            this.ShowMessage(
                             VsixText.Format("Common_RepoRootNotFound",RepoLocator.RepoMarkerFileName),
                             OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        ProcessStartInfo StartInfo=new("dotnet", $"run --project \"{ProjectPath}\""){
            WorkingDirectory=RepoRoot,
            UseShellExecute=false,
        };

        try {
            Process.Start(StartInfo);
        } catch(Exception Ex){
            ActivityLog.LogError(nameof(OpenGameCommand),Ex.ToString());
            this.ShowMessage(VsixText.Format("OpenGame_LaunchFailed",Ex.Message),OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void ShowMessage(string Message,OLEMSGICON Icon){
        VsShellUtilities.ShowMessageBox(this.Package,
            Message,
            VsixText.Get("Common_ProductTitle"),
            Icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
