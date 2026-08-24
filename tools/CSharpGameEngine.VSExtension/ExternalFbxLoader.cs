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
internal static class ExternalFbxLoader {
    private const string BridgeProjectRelativePath=@"tools\CSharpGameEngine.Import.Fbx.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    public static SimpleMesh Load(string FbxPath,string RepoRoot){
        var TempJsonPath=Path.Combine(Path.GetTempPath(), $"csge-fbx-bridge-{Guid.NewGuid():N}.json");
        try {
            // useResidentServer: true - Import.Fbx.Cli is the one bridge with real native-crash risk
            // (AssimpNet). FbxBridgeWorker already serializes the actual import via a SemaphoreSlim
            // (ADR-0023) to remove the concurrency risk; if real-world crash frequency ever becomes
            // a problem, flipping this single flag to false reverts to the proven one-shot path with
            // no other changes needed.
            var Json=ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath,FbxPath,TempJsonPath,RepoRoot,BridgeTimeout,UseResidentServer:true);

            var Document=JsonSerializer.Deserialize<BridgeDocument>(
                                                                   Json,new JsonSerializerOptions { PropertyNameCaseInsensitive=true });

            if(Document is null||!Document.Success){
                throw new InvalidOperationException(VsixText.Get("Bridge_FbxImportFailed"));
            }

            return Document.ToSimpleMesh();
        } finally {
            try { File.Delete(TempJsonPath);} catch(IOException){ } catch(UnauthorizedAccessException){ }
        }
    }
}
