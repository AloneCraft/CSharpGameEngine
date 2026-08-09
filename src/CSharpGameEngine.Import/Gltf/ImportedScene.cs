namespace CSharpGameEngine.Import.Gltf;

public sealed class ImportedScene
{
    public required IReadOnlyList<ImportedSceneNode> Nodes { get; init; }

    public required IReadOnlyList<ImportedMeshData> Meshes { get; init; }

    public required IReadOnlyList<ImportedPbrMaterial> Materials { get; init; }

    public required IReadOnlyList<ImportedLight> Lights { get; init; }
}
