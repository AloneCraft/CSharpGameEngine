using System;
using System.Collections.Concurrent;
using System.IO;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dispatches by file extension, matching <c>CSharpGameEngine.Extension</c>'s CLI viewer
/// (Program.cs). Both .fbx (<see cref="ExternalFbxLoader"/>, ADR-0010) and .glb/.gltf
/// (<see cref="ExternalGltfLoader"/>, ADR-0019) are parsed out-of-process via
/// <see cref="ExternalBridgeProcess"/>, so neither format's parser is ever loaded directly into
/// devenv.exe. Shared by <see cref="AssetPreviewHwndHost"/> (ADR-0013, single-asset preview) and
/// <see cref="SceneHwndHost"/> (ADR-0020, シーン tab) so both load models the same way - the older
/// <c>ViewerHwndHost</c> (ADR-0007/0008) this comment used to also list was removed in ADR-0021.
/// Results are cached for the lifetime of the devenv.exe process, keyed by normalized absolute path
/// and invalidated by last-write-time, so reopening/reloading the same unchanged asset (e.g. every
/// シーン tab reload, or repeated シェイプ palette clicks of the same primitive) never re-launches a
/// bridge process at all - complementing the ADR-0010 addendum fix (prebuilt-exe launch) rather than
/// replacing it, since caching alone cannot help a scene's first, cold load.
/// </summary>
internal static class MeshLoader
{
    private static readonly ConcurrentDictionary<string, (SimpleMesh Mesh, DateTime LastWriteTimeUtc)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SimpleMesh Load(string modelPath, string repoRoot)
    {
        string fullPath = Path.GetFullPath(modelPath);
        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);

        if (Cache.TryGetValue(fullPath, out var cached) && cached.LastWriteTimeUtc == lastWriteTimeUtc)
        {
            return cached.Mesh;
        }

        SimpleMesh mesh = string.Equals(Path.GetExtension(fullPath), ".fbx", StringComparison.OrdinalIgnoreCase)
            ? ExternalFbxLoader.Load(fullPath, repoRoot)
            : ExternalGltfLoader.Load(fullPath, repoRoot);

        Cache[fullPath] = (mesh, lastWriteTimeUtc);
        return mesh;
    }
}
