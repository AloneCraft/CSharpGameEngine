using System.Text.Json;
using CSharpGameEngine.Import.Fbx;
using CSharpGameEngine.Import.Fbx.Cli;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CSharpGameEngine.Import.Fbx.Cli <input.fbx> <output.json>");
    return 2;
}

string inputPath = args[0];
string outputPath = args[1];

FbxImportResult result = FbxImporter.Import(inputPath);

foreach (FbxImportDiagnostic diagnostic in result.Diagnostics)
{
    Console.WriteLine(diagnostic.ToString());
}

FbxBridgeDocument document = result.Success
    ? FbxBridgeDocument.ForMesh(result.Mesh!)
    : FbxBridgeDocument.ForFailure();

string json = JsonSerializer.Serialize(document);
File.WriteAllText(outputPath, json);

return result.Success ? 0 : 1;
