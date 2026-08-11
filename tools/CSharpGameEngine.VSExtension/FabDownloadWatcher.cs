using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Watches the OS downloads folder and copies newly-completed files matching
/// <see cref="WatchedExtensions"/> into the project's <c>UserGameProject/Assets/Fab/</c> folder
/// (ADR-0011). This is a pure "drop zone" convenience - it never talks to Fab, never logs in, and
/// never downloads anything itself. The actual browse/login/purchase/download flow always happens
/// in the user's own browser via <see cref="OpenFabCommand"/>; this class only reacts to files that
/// already landed in the downloads folder as a result of that manual flow.
/// </summary>
internal sealed class FabDownloadWatcher:IDisposable {
    // Deliberately an allow-list rather than "copy everything": the downloads folder also
    // collects unrelated files (installers, PDFs, etc.) that have no business ending up in a game
    // project's Assets folder. These extensions cover the asset formats Fab actually distributes
    // (glTF/FBX/OBJ meshes, common texture formats, and the zip archives Fab often packages
    // multi-file exports as).
    public static readonly string[]WatchedExtensions=[
        ".fbx",".glb",".gltf",".obj",".zip",".png",".jpg",".jpeg",".tga",".usd",".usdz",
    ];

    private readonly FileSystemWatcher Watcher;
    private readonly string DestinationFolder;
    private readonly Action<string>OnFileCopied;
    private readonly Action<string>OnError;

    public string WatchedFolder { get;}

    public bool IsRunning=>Watcher.EnableRaisingEvents;

    public FabDownloadWatcher(string watchedFolder,string DestinationFolder,Action<string>OnFileCopied,Action<string>OnError){
        WatchedFolder=watchedFolder??throw new ArgumentNullException(nameof(watchedFolder));
        this.DestinationFolder=DestinationFolder??throw new ArgumentNullException(nameof(DestinationFolder));
        this.OnFileCopied=OnFileCopied??throw new ArgumentNullException(nameof(OnFileCopied));
        this.OnError=OnError??throw new ArgumentNullException(nameof(OnError));

        Directory.CreateDirectory(DestinationFolder);

        Watcher=new FileSystemWatcher(watchedFolder){
            NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite,
            IncludeSubdirectories=false,
        };
        Watcher.Created+=(_,E)=>HandleCandidate(E.FullPath);
        // Browsers commonly download to a temp name (e.g. "*.crdownload") and rename to the final
        // name only once the download finishes - Renamed is the reliable "file is actually ready"
        // signal for that case; Created alone would fire on the temp file and/or on a truncated
        // partial write.
        Watcher.Renamed+=(_,E)=>HandleCandidate(E.FullPath);
        Watcher.Error+=(_,E)=>OnError(E.GetException()?.Message??"FileSystemWatcher reported an unknown error.");
    }

    public void Start()=>Watcher.EnableRaisingEvents=true;

    public void Stop()=>Watcher.EnableRaisingEvents=false;

    private void HandleCandidate(string path){
        var Extension=Path.GetExtension(path);
        if(!WatchedExtensions.Contains(Extension,StringComparer.OrdinalIgnoreCase)){
            return;
        }

        ThreadPool.QueueUserWorkItem(_=>CopyWhenReady(path));
    }

    private void CopyWhenReady(string SourcePath){
        try {
            if(!WaitUntilReadable(SourcePath,TimeSpan.FromSeconds(30))){
                return;
            }

            var DestinationPath=MakeUnique(Path.Combine(DestinationFolder,Path.GetFileName(SourcePath)));
            File.Copy(SourcePath,DestinationPath,overwrite:false);
            OnFileCopied(DestinationPath);
        } catch(Exception Ex) when (Ex is IOException or UnauthorizedAccessException){
            OnError($"{SourcePath}: {Ex.Message}");
        }
    }

    private static bool WaitUntilReadable(string path,TimeSpan timeout){
        DateTime Deadline=DateTime.UtcNow+timeout;
        while(DateTime.UtcNow<Deadline){
            if(!File.Exists(path)){
                return false;
            }

            try {
                using FileStream stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.None);
                return true;
            } catch(IOException){
                Thread.Sleep(500);
            }
        }

        return false;
    }

    private static string MakeUnique(string path){
        if(!File.Exists(path)){
            return path;
        }

        var directory=Path.GetDirectoryName(path);
        var NameWithoutExtension=Path.GetFileNameWithoutExtension(path);
        var Extension=Path.GetExtension(path);

        var Counter=1;
        string Candidate;
        do {
            Candidate=Path.Combine(directory??string.Empty, $"{NameWithoutExtension} ({Counter}){Extension}");
            Counter++;
        } while(File.Exists(Candidate));

        return Candidate;
    }

    public void Dispose()=>Watcher.Dispose();
}
