using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu/toolbar command that starts or stops <see cref="FabDownloadWatcher"/> (ADR-0011).
/// Purely a local convenience: it copies files the user already downloaded via their own browser
/// session on fab.com into <c>UserGameProject/Assets/Fab/</c>. It never touches fab.com itself -
/// see <see cref="OpenFabCommand"/> for that.
/// </summary>
internal sealed class ToggleFabWatchCommand : IDisposable
{
    public const int CommandId = 0x0102;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string DestinationRelativePath = @"UserGameProject\Assets\Fab";

    private readonly AsyncPackage _package;
    private readonly OleMenuCommand _command;
    private FabDownloadWatcher? _watcher;

    private ToggleFabWatchCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        if (commandService is null)
        {
            throw new ArgumentNullException(nameof(commandService));
        }

        CommandID menuCommandId = new(CommandSet, CommandId);
        _command = new OleMenuCommand(Execute, menuCommandId);
        _command.BeforeQueryStatus += UpdateText;
        commandService.AddCommand(_command);
    }

    public static async Task<ToggleFabWatchCommand> InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        object? service = await package.GetServiceAsync(typeof(IMenuCommandService));
        if (service is not OleMenuCommandService commandService)
        {
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        return new ToggleFabWatchCommand(package, commandService);
    }

    private void UpdateText(object sender, EventArgs e)
    {
        bool running = _watcher?.IsRunning ?? false;
        _command.Text = running ? "Fabダウンロード監視を停止" : "Fabダウンロード監視を開始";
        _command.Checked = running;
    }

    private void Execute(object sender, EventArgs e)
    {
        if (_watcher is { IsRunning: true })
        {
            _watcher.Stop();
            ShowMessage($"Fabダウンロード監視を停止しました。\n監視フォルダ: {_watcher.WatchedFolder}");
            return;
        }

        string? repoRoot = RepoLocator.ResolveRepoRoot();
        if (repoRoot is null)
        {
            ShowMessage($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。", OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        string downloadsFolder;
        try
        {
            downloadsFolder = KnownFolders.GetDownloadsFolder();
        }
        catch (Exception ex)
        {
            ShowMessage($"ダウンロードフォルダの取得に失敗しました: {ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        string destination = Path.Combine(repoRoot, DestinationRelativePath);

        _watcher?.Dispose();
        _watcher = new FabDownloadWatcher(downloadsFolder, destination, OnFileCopied, OnWatchError);
        _watcher.Start();

        ShowMessage(
            "Fabダウンロード監視を開始しました。\n" +
            $"監視フォルダ: {downloadsFolder}\n" +
            $"コピー先: {destination}\n\n" +
            $"対応拡張子: {string.Join(", ", FabDownloadWatcher.WatchedExtensions)}\n\n" +
            "Fab自体へのログイン・ダウンロードはこのウィンドウでは行いません - 常にブラウザ上でご自身の操作で行ってください。");
    }

    private void OnFileCopied(string destinationPath)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async delegate
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (await _package.GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText($"Fab: {Path.GetFileName(destinationPath)} を Assets/Fab にコピーしました。");
            }
        });
    }

    private void OnWatchError(string message)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async delegate
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (await _package.GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText($"Fabダウンロード監視でエラー: {message}");
            }
        });
    }

    private void ShowMessage(string message, OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO)
    {
        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "C# Game Engine Viewer",
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    public void Dispose() => _watcher?.Dispose();
}
