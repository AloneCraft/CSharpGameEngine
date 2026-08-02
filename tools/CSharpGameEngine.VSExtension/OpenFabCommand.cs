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
internal sealed class OpenFabCommand
{
    public const int CommandId = 0x0103;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string FabUrl = "https://www.fab.com/";

    private readonly AsyncPackage _package;

    private OpenFabCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        if (commandService is null)
        {
            throw new ArgumentNullException(nameof(commandService));
        }

        CommandID menuCommandId = new(CommandSet, CommandId);
        MenuCommand menuItem = new(Execute, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        object? service = await package.GetServiceAsync(typeof(IMenuCommandService));
        if (service is not OleMenuCommandService commandService)
        {
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _ = new OpenFabCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(FabUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            VsShellUtilities.ShowMessageBox(
                _package,
                $"ブラウザの起動に失敗しました: {ex.Message}",
                "C# Game Engine Viewer",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
