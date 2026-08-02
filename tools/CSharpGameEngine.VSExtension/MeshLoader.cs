using System;
using System.IO;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dispatches by file extension, matching <c>CSharpGameEngine.Extension</c>'s CLI viewer
/// (Program.cs). .fbx goes through <see cref="ExternalFbxLoader"/> (out-of-process, ADR-0010)
/// rather than an in-process AssimpNet reference, to keep a malformed/malicious .fbx file from
/// being able to crash devenv.exe itself. Shared by <see cref="ViewerHwndHost"/> (ADR-0007/0008)
/// and <see cref="AssetPreviewHwndHost"/> (ADR-0013) so both load models the same way.
/// </summary>
internal static class MeshLoader
{
    public static SimpleMesh Load(string modelPath, string repoRoot) =>
        string.Equals(Path.GetExtension(modelPath), ".fbx", StringComparison.OrdinalIgnoreCase)
            ? ExternalFbxLoader.Load(modelPath, repoRoot)
            : SimpleGltfLoader.Load(modelPath);
}
