using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// OS のダウンロードフォルダーを監視し、ダウンロードが完了した <see cref="WatchedExtensions"/> に一致するファイルをプロジェクトの <c>UserEcsProject/Assets/Fab/</c> フォルダーへコピーします（ADR-0011）。これはファイルの「ドロップゾーン」としての利便機能に限られ、Fab との通信、ログイン、ダウンロードは行いません。実際の閲覧、ログイン、購入、ダウンロードは常にユーザー自身のブラウザーで <see cref="OpenFabCommand"/> を使って行い、このクラスは手動操作の結果としてダウンロードフォルダーに保存されたファイルにのみ反応します。
/// </summary>
internal sealed class FabDownloadWatcher:IDisposable {
    // 「すべてコピーする」方式ではなく、許可リストを使います。ダウンロードフォルダーには無関係なファイルも
    // 集まるためです（インストーラーや PDF など、ゲームプロジェクトの Assets フォルダーに
    // 入れるべきでないものがあります）。以下の拡張子は Fab が実際に配布するアセット形式を対象にし、
    // glTF/FBX/OBJ のメッシュ、一般的なテクスチャ形式、および Fab がよく複数ファイルの出力をまとめる
    // zip アーカイブを含みます。
    public static readonly string[]WatchedExtensions=[
        ".fbx",".glb",".gltf",".obj",".zip",".png",".jpg",".jpeg",".tga",".usd",".usdz",
    ];

    private readonly FileSystemWatcher Watcher;
    private readonly string DestinationFolder;
    private readonly Action<string>OnFileCopied;
    private readonly Action<string>OnError;

    public string WatchedFolder { get;}

    public bool IsRunning=> this.Watcher.EnableRaisingEvents;

    public FabDownloadWatcher(string WatchedFolder,string DestinationFolder,Action<string>OnFileCopied,Action<string>OnError){
        this.WatchedFolder= WatchedFolder ?? throw new ArgumentNullException(nameof(WatchedFolder));
        this.DestinationFolder=DestinationFolder??throw new ArgumentNullException(nameof(DestinationFolder));
        this.OnFileCopied=OnFileCopied??throw new ArgumentNullException(nameof(OnFileCopied));
        this.OnError=OnError??throw new ArgumentNullException(nameof(OnError));

        Directory.CreateDirectory(DestinationFolder);

        this.Watcher=new FileSystemWatcher(WatchedFolder){
                                                             NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite,
                                                             IncludeSubdirectories=false,
                                                         };
        this.Watcher.Created+=(_,E)=> this.HandleCandidate(E.FullPath);
        // ブラウザーはダウンロード中に一時名（例："*.crdownload"）をよく使い、完了時にだけ最終名へ変更します。
        // その場合、Renamed が「ファイルの準備完了」を示す確実な通知です。Created だけでは一時ファイルや
        // 不完全なファイルを検出してしまいます。
        // 書き込みの途中。
        this.Watcher.Renamed+=(_,E)=> this.HandleCandidate(E.FullPath);
        this.Watcher.Error+=(_,E)=>OnError(E.GetException()?.Message??"FileSystemWatcher reported an unknown error.");
    }

    public void Start()=> this.Watcher.EnableRaisingEvents=true;

    public void Stop()=> this.Watcher.EnableRaisingEvents=false;

    private void HandleCandidate(string Path){
        var Extension= System.IO.Path.GetExtension(Path);
        if(!WatchedExtensions.Contains(Extension,StringComparer.OrdinalIgnoreCase)){
            return;
        }

        ThreadPool.QueueUserWorkItem(_=> this.CopyWhenReady(Path));
    }

    private void CopyWhenReady(string SourcePath){
        try {
            if(!WaitUntilReadable(SourcePath,TimeSpan.FromSeconds(30))){
                return;
            }

            var DestinationPath=MakeUnique(Path.Combine(this.DestinationFolder,Path.GetFileName(SourcePath)));
            File.Copy(SourcePath,DestinationPath,overwrite:false);
            this.OnFileCopied(DestinationPath);
        } catch(Exception Ex) when (Ex is IOException or UnauthorizedAccessException){
            this.OnError($"{SourcePath}: {Ex.Message}");
        }
    }

    private static bool WaitUntilReadable(string Path,TimeSpan Timeout){
        DateTime Deadline=DateTime.UtcNow+Timeout;
        while(DateTime.UtcNow<Deadline){
            if(!File.Exists(Path)){
                return false;
            }

            try {
                using FileStream Stream=File.Open(Path,FileMode.Open,FileAccess.Read,FileShare.None);
                return true;
            } catch(IOException){
                Thread.Sleep(500);
            }
        }

        return false;
    }

    private static string MakeUnique(string Path){
        if(!File.Exists(Path)){
            return Path;
        }

        var Directory= System.IO.Path.GetDirectoryName(Path);
        var NameWithoutExtension= System.IO.Path.GetFileNameWithoutExtension(Path);
        var Extension= System.IO.Path.GetExtension(Path);

        var Counter=1;
        string Candidate;
        do {
            Candidate= System.IO.Path.Combine(Directory ?? string.Empty, $"{NameWithoutExtension} ({Counter}){Extension}");
            Counter++;
        } while(File.Exists(Candidate));

        return Candidate;
    }

    public void Dispose()=> this.Watcher.Dispose();
}
