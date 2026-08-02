using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Generates a Unity/UE-style <c>&lt;file&gt;.meta</c> sidecar (a stable GUID, nothing else - ADR-0012)
/// next to every 3D asset under <c>UserGameProject/Assets</c>. Runs automatically whenever the VSIX
/// package loads (no user toggle, unlike <see cref="FabDownloadWatcher"/> - this is pure bookkeeping
/// with no privacy/intentionality concerns): an initial scan covers files already present, then a
/// <see cref="FileSystemWatcher"/> covers anything added afterward, regardless of how it got there
/// (Fab download watcher, manual copy, "Add Existing Item", etc). Never overwrites an existing
/// <c>.meta</c> - the whole point of the GUID is that it stays stable once assigned.
/// </summary>
internal sealed class AssetMetaWatcher : IDisposable
{
    public static readonly string[] WatchedExtensions = [".glb", ".gltf", ".fbx"];

    private readonly FileSystemWatcher _watcher;

    private AssetMetaWatcher(string assetsRoot)
    {
        foreach (string existingAsset in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
        {
            GenerateMetaIfMissing(existingAsset);
        }

        _watcher = new FileSystemWatcher(assetsRoot)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
        };
        _watcher.Created += (_, e) => HandleCandidate(e.FullPath);
        _watcher.Renamed += (_, e) => HandleCandidate(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Resolves the repo root and starts watching <c>UserGameProject/Assets</c> if it exists.
    /// Returns <see langword="null"/> (does nothing) if the repo root or Assets folder can't be
    /// found - never throws, since this runs unconditionally on package load.
    /// </summary>
    public static AssetMetaWatcher? StartIfProjectExists()
    {
        string? repoRoot = RepoLocator.ResolveRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        string assetsRoot = Path.Combine(repoRoot, "UserGameProject", "Assets");
        return Directory.Exists(assetsRoot) ? new AssetMetaWatcher(assetsRoot) : null;
    }

    private void HandleCandidate(string path)
    {
        if (!WatchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => GenerateMetaWhenReady(path));
    }

    private static void GenerateMetaWhenReady(string assetPath)
    {
        if (!WaitUntilReadable(assetPath, TimeSpan.FromSeconds(30)))
        {
            return;
        }

        try
        {
            GenerateMetaIfMissing(assetPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void GenerateMetaIfMissing(string assetPath)
    {
        if (!WatchedExtensions.Contains(Path.GetExtension(assetPath), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        string metaPath = assetPath + ".meta";
        if (File.Exists(metaPath))
        {
            return;
        }

        string guid = Guid.NewGuid().ToString("N");
        File.WriteAllText(metaPath, $"guid: {guid}\n");
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
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
        }

        return false;
    }

    public void Dispose() => _watcher.Dispose();
}
