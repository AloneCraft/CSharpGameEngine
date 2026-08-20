using System.Text.Json;
using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Import.Gltf.Cli;

/// <summary>
/// The actual import work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode and
/// the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share identical
/// logic. Returns diagnostics as a value instead of calling <see cref="Console"/> directly - those
/// are process-wide statics that would race across the resident server's concurrent requests.
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
