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
internal sealed class OpenViewerCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string ViewerModelArgument = "Samples/Box.glb";

    private readonly AsyncPackage _package;

    private OpenViewerCommand(AsyncPackage package, OleMenuCommandService commandService)
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

        _ = new OpenViewerCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        string? repoRoot = RepoLocator.ResolveRepoRoot();
        if (repoRoot is null)
        {
            ShowMessage(
                $"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。このVSIXは開発中のスケルトンであり、" +
                "ビルド時に埋め込んだパスでのみ動作します。詳細はADR-0006を参照してください。",
                OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        ProcessStartInfo startInfo = new("dotnet", $"run --project tools/CSharpGameEngine.Extension -- --model {ViewerModelArgument}")
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
            ShowMessage($"ビューアの起動に失敗しました: {ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "C# Game Engine Viewer",
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
