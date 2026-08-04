using System;
using System.IO;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Loads an .fbx file for the VSIX tool window WITHOUT loading AssimpNet's native
/// <c>assimp.dll</c> into <c>devenv.exe</c> (ADR-0010). FBX parsing runs in a short-lived
/// out-of-process helper (<c>CSharpGameEngine.Import.Fbx.Cli</c>, net10.0, wraps
/// <c>CSharpGameEngine.Import.Fbx</c>/AssimpNet), launched via the shared
/// <see cref="ExternalBridgeProcess"/> runner (ADR-0019, also used by <see cref="ExternalGltfLoader"/>).
/// A crash or hang in the native FBX parser while parsing an untrusted .fbx file is contained to the
/// helper process and surfaces here as a plain exception (caught by
/// <see cref="ViewerHwndHost.BuildWindowCore"/>) instead of taking down Visual Studio itself.
/// </summary>
internal static class ExternalFbxLoader
{
    private const string BridgeProjectRelativePath = @"tools\CSharpGameEngine.Import.Fbx.Cli";
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(60);

    public static SimpleMesh Load(string fbxPath, string repoRoot)
    {
        string tempJsonPath = Path.Combine(Path.GetTempPath(), $"csge-fbx-bridge-{Guid.NewGuid():N}.json");
        try
        {
            string json = ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath, fbxPath, tempJsonPath, repoRoot, BridgeTimeout);

            BridgeDocument? document = JsonSerializer.Deserialize<BridgeDocument>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (document is null || !document.Success)
            {
                throw new InvalidOperationException("FBXファイルのインポートに失敗しました(詳細はブリッジプロセスの出力を確認してください)。");
            }

            return document.ToSimpleMesh();
        }
        finally
        {
            try { File.Delete(tempJsonPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
