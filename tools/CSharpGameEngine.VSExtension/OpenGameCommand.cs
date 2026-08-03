using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that launches the ECS-driven stage vertical slice (<c>UserGameProject</c>,
/// ADR-0016) as a separate top-level window - the exact same "spawn a separate net10.0 process,
/// don't embed it" mechanism as <see cref="OpenViewerCommand"/> (ADR-0006), chosen deliberately
/// over a docked tool window: <c>CSharpGameEngine.Ecs</c> uses C# 11 static abstract interface
/// members, which the .NET Framework 4.7.2 CLR this VSIX runs on does not support at all, so it
/// cannot be referenced or run in-process here the way <see cref="ViewerHwndHost"/>/
/// <see cref="VorticeRenderer"/> run the glTF viewer in-process. Embedding a separate process's
/// window into a docked VS tool window (HWND reparenting or a shared-texture bridge) is real,
/// unbuilt work with no precedent in this codebase yet and is deliberately deferred; see ADR-0016.
/// Contains no rendering/engine/ECS logic itself.
/// </summary>
internal sealed class OpenGameCommand
{
    public const int CommandId = 0x0104;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

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
                $"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。このVSIXは開発中のスケルトンであり、" +
                "ビルド時に埋め込んだパスでのみ動作します。詳細はADR-0006を参照してください。",
                OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        ProcessStartInfo startInfo = new("dotnet", "run --project UserGameProject")
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
            ShowMessage($"ゲームの起動に失敗しました: {ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
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
