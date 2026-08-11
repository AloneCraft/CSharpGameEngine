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

    private readonly FileSystemWatcher Watcher;

    private AssetMetaWatcher(string AssetsRoot)
    {
        foreach (string ExistingAsset in Directory.EnumerateFiles(AssetsRoot, "*", SearchOption.AllDirectories))
        {
            GenerateMetaIfMissing(ExistingAsset);
        }

        Watcher = new FileSystemWatcher(AssetsRoot)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
        };
        Watcher.Created += (_, E) => HandleCandidate(E.FullPath);
        Watcher.Renamed += (_, E) => HandleCandidate(E.FullPath);
        Watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Resolves the repo root and starts watching <c>UserGameProject/Assets</c> if it exists.
    /// Returns <see langword="null"/> (does nothing) if the repo root or Assets folder can't be
    /// found - never throws, since this runs unconditionally on package load.
    /// </summary>
    public static AssetMetaWatcher? StartIfProjectExists()
    {
        string? RepoRoot = RepoLocator.ResolveRepoRoot();
        if (RepoRoot is null)
        {
            return null;
        }

        string AssetsRoot = Path.Combine(RepoRoot, "UserGameProject", "Assets");
        return Directory.Exists(AssetsRoot) ? new AssetMetaWatcher(AssetsRoot) : null;
    }

    private void HandleCandidate(string path)
    {
        if (!WatchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => GenerateMetaWhenReady(path));
    }

    private static void GenerateMetaWhenReady(string AssetPath)
    {
        if (!WaitUntilReadable(AssetPath, TimeSpan.FromSeconds(30)))
        {
            return;
        }

        try
        {
            GenerateMetaIfMissing(AssetPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void GenerateMetaIfMissing(string AssetPath)
    {
        if (!WatchedExtensions.Contains(Path.GetExtension(AssetPath), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        string MetaPath = AssetPath + ".meta";
        if (File.Exists(MetaPath))
        {
            return;
        }

        string guid = Guid.NewGuid().ToString("N");
        File.WriteAllText(MetaPath, $"guid: {guid}\n");
    }

    private static bool WaitUntilReadable(string path, TimeSpan timeout)
    {
        DateTime Deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < Deadline)
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

    public void Dispose() => Watcher.Dispose();
}
