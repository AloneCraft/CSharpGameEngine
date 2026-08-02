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
internal sealed class FabDownloadWatcher : IDisposable
{
    // Deliberately an allow-list rather than "copy everything": the downloads folder also
    // collects unrelated files (installers, PDFs, etc.) that have no business ending up in a game
    // project's Assets folder. These extensions cover the asset formats Fab actually distributes
    // (glTF/FBX/OBJ meshes, common texture formats, and the zip archives Fab often packages
    // multi-file exports as).
    public static readonly string[] WatchedExtensions =
    [
        ".fbx", ".glb", ".gltf", ".obj", ".zip", ".png", ".jpg", ".jpeg", ".tga", ".usd", ".usdz",
    ];

    private readonly FileSystemWatcher _watcher;
    private readonly string _destinationFolder;
    private readonly Action<string> _onFileCopied;
    private readonly Action<string> _onError;

    public string WatchedFolder { get; }

    public bool IsRunning => _watcher.EnableRaisingEvents;

    public FabDownloadWatcher(string watchedFolder, string destinationFolder, Action<string> onFileCopied, Action<string> onError)
    {
        WatchedFolder = watchedFolder ?? throw new ArgumentNullException(nameof(watchedFolder));
        _destinationFolder = destinationFolder ?? throw new ArgumentNullException(nameof(destinationFolder));
        _onFileCopied = onFileCopied ?? throw new ArgumentNullException(nameof(onFileCopied));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));

        Directory.CreateDirectory(_destinationFolder);

        _watcher = new FileSystemWatcher(watchedFolder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };
        _watcher.Created += (_, e) => HandleCandidate(e.FullPath);
        // Browsers commonly download to a temp name (e.g. "*.crdownload") and rename to the final
        // name only once the download finishes - Renamed is the reliable "file is actually ready"
        // signal for that case; Created alone would fire on the temp file and/or on a truncated
        // partial write.
        _watcher.Renamed += (_, e) => HandleCandidate(e.FullPath);
        _watcher.Error += (_, e) => _onError(e.GetException()?.Message ?? "FileSystemWatcher reported an unknown error.");
    }

    public void Start() => _watcher.EnableRaisingEvents = true;

    public void Stop() => _watcher.EnableRaisingEvents = false;

    private void HandleCandidate(string path)
    {
        string extension = Path.GetExtension(path);
        if (!WatchedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => CopyWhenReady(path));
    }

    private void CopyWhenReady(string sourcePath)
    {
        try
        {
            if (!WaitUntilReadable(sourcePath, TimeSpan.FromSeconds(30)))
            {
                return;
            }

            string destinationPath = MakeUnique(Path.Combine(_destinationFolder, Path.GetFileName(sourcePath)));
            File.Copy(sourcePath, destinationPath, overwrite: false);
            _onFileCopied(destinationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onError($"{sourcePath}: {ex.Message}");
        }
    }

    private static bool WaitUntilReadable(string path, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
        }

        return false;
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string? directory = Path.GetDirectoryName(path);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory ?? string.Empty, $"{nameWithoutExtension} ({counter}){extension}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }

    public void Dispose() => _watcher.Dispose();
}
