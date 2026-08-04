using System;
using System.IO;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dispatches by file extension, matching <c>CSharpGameEngine.Extension</c>'s CLI viewer
/// (Program.cs). Both .fbx (<see cref="ExternalFbxLoader"/>, ADR-0010) and .glb/.gltf
/// (<see cref="ExternalGltfLoader"/>, ADR-0019) are parsed out-of-process via
/// <see cref="ExternalBridgeProcess"/>, so neither format's parser is ever loaded directly into
/// devenv.exe. Shared by <see cref="ViewerHwndHost"/> (ADR-0007/0008), <see cref="AssetPreviewHwndHost"/>
/// (ADR-0013), and <see cref="SceneHwndHost"/> so all three load models the same way.
/// </summary>
internal static class MeshLoader
{
    public static SimpleMesh Load(string modelPath, string repoRoot) =>
        string.Equals(Path.GetExtension(modelPath), ".fbx", StringComparison.OrdinalIgnoreCase)
            ? ExternalFbxLoader.Load(modelPath, repoRoot)
            : ExternalGltfLoader.Load(modelPath, repoRoot);
}
