using System;
using System.IO;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Loads a .glb/.gltf file for the VSIX tool window via a short-lived out-of-process helper
/// (<c>CSharpGameEngine.Import.Gltf.Cli</c>, net10.0, wraps <c>CSharpGameEngine.Import.Gltf</c>/
/// SharpGLTF.Core), launched via the shared <see cref="ExternalBridgeProcess"/> runner (ADR-0019).
/// Unifies glTF loading with <see cref="ExternalFbxLoader"/>'s process-isolation approach (ADR-0010)
/// instead of the previous state where FBX alone was process-isolated and glTF was parsed in-process
/// via SharpGLTF.Core directly inside <c>devenv.exe</c> - that asymmetry was deliberately removed.
/// </summary>
internal static class ExternalGltfLoader
{
    private const string BridgeProjectRelativePath = @"tools\CSharpGameEngine.Import.Gltf.Cli";
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(60);

    public static SimpleMesh Load(string gltfPath, string repoRoot)
    {
        string tempJsonPath = Path.Combine(Path.GetTempPath(), $"csge-gltf-bridge-{Guid.NewGuid():N}.json");
        try
        {
            string json = ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath, gltfPath, tempJsonPath, repoRoot, BridgeTimeout);

            BridgeDocument? document = JsonSerializer.Deserialize<BridgeDocument>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (document is null || !document.Success)
            {
                throw new InvalidOperationException("glTFファイルのインポートに失敗しました(詳細はブリッジプロセスの出力を確認してください)。");
            }

            return document.ToSimpleMesh();
        }
        finally
        {
            try { File.Delete(tempJsonPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
