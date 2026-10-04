using System.Text.Json;
using CSharpGameEngine.Import.Fbx;

namespace CSharpGameEngine.Import.Fbx.Cli;

/// <summary>
/// The actual import work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode and
/// the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share identical
/// logic. Returns diagnostics as a value instead of calling <see cref="Console"/> directly - those
/// are process-wide statics that would race across the resident server's concurrent requests.
///
/// The resident server can have several FBX imports in flight at once (e.g. a scene referencing
/// multiple .fbx assets, loaded in parallel by SceneHwndHost). <see cref="FbxImporter.Import"/>
/// serializes the part that holds AssimpNet's process-wide log stream itself, so concurrent
/// requests neither crash nor cross-contaminate diagnostics.
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
