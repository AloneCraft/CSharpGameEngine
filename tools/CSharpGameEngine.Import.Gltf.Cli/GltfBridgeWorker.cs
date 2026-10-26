using System.Text.Json;
using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Import.Gltf.Cli;

/// <summary>
/// <c>Program.cs</c> から切り出した実際のインポート処理 (ADR-0023)。1 回限りのモードと常駐サーバー（<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>）の両方が同じロジックを共有する。<see cref="Console"/> を直接呼び出す代わりに、診断を値として返す。Console はプロセス全体の静的データであり、常駐サーバーの並行要求間で競合するためである。
/// </summary>
internal static class GltfBridgeWorker {
    public static(int ExitCode,string Output)Run(string InputPath,string OutputPath){
        var Result=GltfImporter.Import(InputPath);

        List<string>Lines=[];
        foreach(var Diagnostic in Result.Diagnostics){
            Lines.Add(Diagnostic.ToString()??string.Empty);
        }

        var Document=Result.Success
                         ?GltfBridgeDocument.ForMesh(Result.Mesh!)
                         :GltfBridgeDocument.ForFailure();

        var Json=JsonSerializer.Serialize(Document);
        File.WriteAllText(OutputPath,Json);

        return(Result.Success?0:1,string.Join(Environment.NewLine,Lines));
    }
}
