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

    public static SimpleMesh Load(string GltfPath, string RepoRoot)
    {
        string TempJsonPath = Path.Combine(Path.GetTempPath(), $"csge-gltf-bridge-{Guid.NewGuid():N}.json");
        try
        {
            string Json = ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath, GltfPath, TempJsonPath, RepoRoot, BridgeTimeout, UseResidentServer: true);

            BridgeDocument? Document = JsonSerializer.Deserialize<BridgeDocument>(
                Json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (Document is null || !Document.Success)
            {
                throw new InvalidOperationException("glTFファイルのインポートに失敗しました(詳細はブリッジプロセスの出力を確認してください)。");
            }

            return Document.ToSimpleMesh();
        }
        finally
        {
            try { File.Delete(TempJsonPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
