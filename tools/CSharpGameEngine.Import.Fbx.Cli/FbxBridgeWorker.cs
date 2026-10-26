using System.Text.Json;
using CSharpGameEngine.Import.Fbx;

namespace CSharpGameEngine.Import.Fbx.Cli;

/// <summary>
/// <c>Program.cs</c> から切り出した実際のインポート処理 (ADR-0023)。1 回限りのモードと常駐サーバー（<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>）の両方が同じロジックを共有する。<see cref="Console"/> を直接呼び出す代わりに、診断を値として返す。Console はプロセス全体の静的データであり、常駐サーバーの並行要求間で競合するためである。
///
/// 常駐サーバーでは複数の FBX インポートを同時に処理できる（例：SceneHwndHost が並列読み込みする複数の .fbx アセットをシーンが参照する場合）。<see cref="FbxImporter.Import"/> は AssimpNet のプロセス共有ログストリームを扱う処理を直列化するため、要求の並行実行でクラッシュしたり診断が混ざったりしない。
/// </summary>
internal static class FbxBridgeWorker {
    public static(int ExitCode,string Output)Run(string InputPath,string OutputPath){
        FbxImportResult Result=FbxImporter.Import(InputPath);

        List<string>Lines=[];
        foreach(var Diagnostic in Result.Diagnostics){
            Lines.Add(Diagnostic.ToString()??string.Empty);
        }

        var Document=Result.Success
                         ?FbxBridgeDocument.ForMesh(Result.Mesh!)
                         :FbxBridgeDocument.ForFailure();

        var Json=JsonSerializer.Serialize(Document);
        File.WriteAllText(OutputPath,Json);

        return(Result.Success?0:1,string.Join(Environment.NewLine,Lines));
    }
}
