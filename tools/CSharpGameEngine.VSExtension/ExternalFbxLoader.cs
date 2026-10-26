using System;
using System.IO;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSIX ツールウィンドウで .fbx ファイルを読み込みます。この処理では AssimpNet のネイティブ <c>assimp.dll</c> を <c>devenv.exe</c> にロードしません（ADR-0010）。FBX の解析は短時間で終了する別プロセスの helper（<c>CSharpGameEngine.Import.Fbx.Cli</c>、net10.0、<c>CSharpGameEngine.Import.Fbx</c>/AssimpNet をラップ）で実行し、共通の <see cref="ExternalBridgeProcess"/> runner（ADR-0019。<see cref="ExternalGltfLoader"/> も使用）経由で起動します。信頼できない .fbx ファイルの解析中にネイティブ FBX parser がクラッシュまたは停止しても helper プロセス内に封じ込められ、Visual Studio 自体を終了させず、ここで通常の例外として通知されます（<see cref="ViewerHwndHost.BuildWindowCore"/> で捕捉）。
/// </summary>
internal static class ExternalFbxLoader {
    private const string BridgeProjectRelativePath=@"tools\CSharpGameEngine.Import.Fbx.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    public static SimpleMesh Load(string FbxPath,string RepoRoot){
        var TempJsonPath=Path.Combine(Path.GetTempPath(), $"csge-fbx-bridge-{Guid.NewGuid():N}.json");
        try {
            // useResidentServer: true。Import.Fbx.Cli は実際のネイティブクラッシュリスク（AssimpNet）を持つ唯一の bridge です。
            // FbxBridgeWorker はすでに SemaphoreSlim で実際のインポートを直列化し、
            // 並行実行のリスクを取り除いています（ADR-0023）。実環境でクラッシュ頻度が
            // 問題になる場合は、このフラグ1つを false にするだけで、実績のある単発実行経路へ戻せます。
            // ほかの変更は必要ありません。
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
