namespace CSharpGameEngine.Import.Gltf;

public sealed class GltfSceneImportResult
{
    private GltfSceneImportResult(bool success, ImportedScene? scene, IReadOnlyList<GltfImportDiagnostic> diagnostics)
    {
        Success = success;
        Scene = scene;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }

    /// <summary>The imported scene. Always <see langword="null"/> when <see cref="Success"/> is false.</summary>
    public ImportedScene? Scene { get; }

    public IReadOnlyList<GltfImportDiagnostic> Diagnostics { get; }

    public static GltfSceneImportResult Succeeded(ImportedScene scene, IReadOnlyList<GltfImportDiagnostic> diagnostics) =>
        new(true, scene, diagnostics);

    public static GltfSceneImportResult Failed(IReadOnlyList<GltfImportDiagnostic> diagnostics) =>
        new(false, null, diagnostics);
}
