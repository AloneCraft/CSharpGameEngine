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
internal sealed class OpenGameCommand
{
    public const int CommandId = 0x0104;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string ProjectPath = "UserEcsProject";

    private readonly AsyncPackage _package;

    private OpenGameCommand(AsyncPackage package, OleMenuCommandService commandService)
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

        _ = new OpenGameCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        string? repoRoot = RepoLocator.ResolveRepoRoot();
        if (repoRoot is null)
        {
            ShowMessage(
                $"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。",
                OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        ProcessStartInfo startInfo = new("dotnet", $"run --project \"{ProjectPath}\"")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ShowMessage($"UserEcsProjectの起動に失敗しました: {ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "C# Game Engine",
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
