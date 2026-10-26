using System;
using System.IO;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX ツールウィンドウ向けに、短時間で終了する別プロセスの helper（<c>CSharpGameEngine.Import.Gltf.Cli</c>、net10.0、<c>CSharpGameEngine.Import.Gltf</c>/SharpGLTF.Core をラップ）を使って .glb/.gltf ファイルを読み込みます。共通の <see cref="ExternalBridgeProcess"/> runner（ADR-0019）で起動します。
/// glTF の読み込みを <see cref="ExternalFbxLoader"/> のプロセス分離方式（ADR-0010）に統一します。以前は FBX だけをプロセス分離し、glTF は <c>devenv.exe</c> 内で SharpGLTF.Core により直接解析する非対称な状態でしたが、これを意図的に解消しました。
/// </summary>
internal static class ExternalGltfLoader {
    private const string BridgeProjectRelativePath=@"tools\CSharpGameEngine.Import.Gltf.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    public static SimpleMesh Load(string GltfPath,string RepoRoot){
        var TempJsonPath=Path.Combine(Path.GetTempPath(), $"csge-gltf-bridge-{Guid.NewGuid():N}.json");
        try {
            var Json=ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath,GltfPath,TempJsonPath,RepoRoot,BridgeTimeout,UseResidentServer:true);

            var Document=JsonSerializer.Deserialize<BridgeDocument>(
                                                                   Json,new JsonSerializerOptions { PropertyNameCaseInsensitive=true });

            if(Document is null||!Document.Success){
                throw new InvalidOperationException(VsixText.Get("Bridge_GltfImportFailed"));
            }

            return Document.ToSimpleMesh();
        } finally {
            try { File.Delete(TempJsonPath);} catch(IOException){ } catch(UnauthorizedAccessException){ }
        }
    }
}
