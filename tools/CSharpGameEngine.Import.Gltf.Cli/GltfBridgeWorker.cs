using System.Text.Json;
using CSharpGameEngine.Import.Gltf;

namespace CSharpGameEngine.Import.Gltf.Cli;

/// <summary>
/// The actual import work, extracted from <c>Program.cs</c> (ADR-0023) so both one-shot mode and
/// the resident server (<c>CSharpGameEngine.Bridge.Hosting.BridgeServerHost</c>) share identical
/// logic. Returns diagnostics as a value instead of calling <see cref="Console"/> directly - those
/// are process-wide statics that would race across the resident server's concurrent requests.
/// </summary>
internal static class GltfBridgeWorker
{
    public static (int ExitCode, string Output) Run(string inputPath, string outputPath)
    {
        GltfImportResult result = GltfImporter.Import(inputPath);

        List<string> lines = [];
        foreach (GltfImportDiagnostic diagnostic in result.Diagnostics)
        {
            lines.Add(diagnostic.ToString() ?? string.Empty);
        }

        GltfBridgeDocument document = result.Success
            ? GltfBridgeDocument.ForMesh(result.Mesh!)
            : GltfBridgeDocument.ForFailure();

        string json = JsonSerializer.Serialize(document);
        File.WriteAllText(outputPath, json);

        return (result.Success ? 0 : 1, string.Join(Environment.NewLine, lines));
    }
}
