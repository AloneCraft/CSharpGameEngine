using System.Text.Json;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Import.Gltf.Cli;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.Import.Gltf.Cli <input.glb|input.gltf> <output.json>");
    return 2;
}

string inputPath = args[0];
string outputPath = args[1];

GltfImportResult result = GltfImporter.Import(inputPath);

foreach (GltfImportDiagnostic diagnostic in result.Diagnostics)
{
    Console.WriteLine(diagnostic.ToString());
}

GltfBridgeDocument document = result.Success
    ? GltfBridgeDocument.ForMesh(result.Mesh!)
    : GltfBridgeDocument.ForFailure();

string json = JsonSerializer.Serialize(document);
File.WriteAllText(outputPath, json);

return result.Success ? 0 : 1;
