using System.Text.Json;
using CSharpGameEngine.Import.Fbx;

namespace CSharpGameEngine.Import.Fbx.Cli;

/// <summary>
/// The actual import work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode and
/// the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share identical
/// logic. Returns diagnostics as a value instead of calling <see cref="Console"/> directly - those
/// are process-wide statics that would race across the resident server's concurrent requests.
///
/// <see cref="FbxImporter.Import"/> calls AssimpNet's <c>LogStream.Attach()</c>/<c>Detach()</c>
/// around each import, which is process-wide native logging state, not per-call. In one-shot mode
/// only one import ever runs per process, so this was never observable; the resident server can
/// have several FBX imports in flight at once (e.g. a scene referencing multiple .fbx assets,
/// loaded in parallel by SceneHwndHost), where concurrent Attach/Detach calls could interleave and
/// cross-contaminate diagnostics or worse. <see cref="ImportGate"/> serializes the import itself so
/// this can never happen - the rest of resident mode's speed benefit (skipping process/CLR startup)
/// is unaffected.
/// </summary>
internal static class FbxBridgeWorker
{
    private static readonly SemaphoreSlim ImportGate = new(1, 1);

    public static (int ExitCode, string Output) Run(string InputPath, string OutputPath)
    {
        FbxImportResult Result;
        ImportGate.Wait();
        try
        {
            Result = FbxImporter.Import(InputPath);
        }
        finally
        {
            ImportGate.Release();
        }

        List<string> Lines = [];
        foreach (FbxImportDiagnostic Diagnostic in Result.Diagnostics)
        {
            Lines.Add(Diagnostic.ToString() ?? string.Empty);
        }

        FbxBridgeDocument Document = Result.Success
            ? FbxBridgeDocument.ForMesh(Result.Mesh!)
            : FbxBridgeDocument.ForFailure();

        string Json = JsonSerializer.Serialize(Document);
        File.WriteAllText(OutputPath, Json);

        return (Result.Success ? 0 : 1, string.Join(Environment.NewLine, Lines));
    }
}
