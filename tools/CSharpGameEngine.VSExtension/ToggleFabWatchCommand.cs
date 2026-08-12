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
/// session on fab.com into <c>UserEcsProject/Assets/Fab/</c>. It never touches fab.com itself -
/// see <see cref="OpenFabCommand"/> for that.
/// </summary>
internal sealed class ToggleFabWatchCommand:IDisposable {
    public const int CommandId=0x0102;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private const string DestinationRelativePath=@"UserEcsProject\Assets\Fab";

    private readonly AsyncPackage Package;
    private readonly OleMenuCommand Command;
    private FabDownloadWatcher?Watcher;

    private ToggleFabWatchCommand(AsyncPackage Package,OleMenuCommandService CommandService){
        this.Package= Package ?? throw new ArgumentNullException(nameof(Package));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        Command=new OleMenuCommand(Execute,MenuCommandId);
        Command.BeforeQueryStatus+=UpdateText;
        CommandService.AddCommand(Command);
    }

    public static async Task<ToggleFabWatchCommand>InitializeAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service= await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        return new ToggleFabWatchCommand(Package,CommandService);
    }

    private void UpdateText(object Sender,EventArgs E){
        var Running=Watcher?.IsRunning??false;
        Command.Text=Running?"Fabダウンロード監視を停止":"Fabダウンロード監視を開始";
        Command.Checked=Running;
    }

    private void Execute(object Sender,EventArgs E){
        if(Watcher is { IsRunning:true }){
            Watcher.Stop();
            ShowMessage($"Fabダウンロード監視を停止しました。\n監視フォルダ: {Watcher.WatchedFolder}");
            return;
        }

        var RepoRoot=RepoLocator.ResolveRepoRoot();
        if(RepoRoot is null){
            ShowMessage($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。",OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        string DownloadsFolder;
        try {
            DownloadsFolder=KnownFolders.GetDownloadsFolder();
        } catch(Exception Ex){
            ShowMessage($"ダウンロードフォルダの取得に失敗しました: {Ex.Message}",OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        var Destination=Path.Combine(RepoRoot,DestinationRelativePath);

        Watcher?.Dispose();
        Watcher=new FabDownloadWatcher(DownloadsFolder,Destination,OnFileCopied,OnWatchError);
        Watcher.Start();

        ShowMessage(
            "Fabダウンロード監視を開始しました。\n"+
            $"監視フォルダ: {DownloadsFolder}\n" +
            $"コピー先: {Destination}\n\n" +
            $"対応拡張子: {string.Join(", ",FabDownloadWatcher.WatchedExtensions)}\n\n" +
            "Fab自体へのログイン・ダウンロードはこのウィンドウでは行いません - 常にブラウザ上でご自身の操作で行ってください。");
    }

    private void OnFileCopied(string DestinationPath){
        _=Package.JoinableTaskFactory.RunAsync(async delegate {
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(await Package.GetServiceAsync(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
                StatusBar.SetText($"Fab: {Path.GetFileName(DestinationPath)} を Assets/Fab にコピーしました。");
            }
        });
    }

    private void OnWatchError(string Message){
        _=Package.JoinableTaskFactory.RunAsync(async delegate {
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(await Package.GetServiceAsync(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
                StatusBar.SetText($"Fabダウンロード監視でエラー: {Message}");
            }
        });
    }

    private void ShowMessage(string Message,OLEMSGICON Icon=OLEMSGICON.OLEMSGICON_INFO){
        VsShellUtilities.ShowMessageBox(
            Package,
            Message,
            "C# Game Engine Viewer",
            Icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    public void Dispose()=>Watcher?.Dispose();
}
