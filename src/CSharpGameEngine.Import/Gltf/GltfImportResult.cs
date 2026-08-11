namespace CSharpGameEngine.Import.Gltf;

public sealed class GltfImportResult {
    private GltfImportResult(bool success,ImportedMesh?mesh,IReadOnlyList<GltfImportDiagnostic>diagnostics){
        Success=success;
        Mesh=mesh;
        Diagnostics=diagnostics;
    }

    public bool Success { get;}

    /// <summary>The imported mesh. Always <see langword="null"/> when <see cref="Success"/> is false.</summary>
    public ImportedMesh?Mesh { get;}

    public IReadOnlyList<GltfImportDiagnostic>Diagnostics { get;}

    public static GltfImportResult Succeeded(ImportedMesh mesh,IReadOnlyList<GltfImportDiagnostic>diagnostics)=>
        new(true,mesh,diagnostics);

    public static GltfImportResult Failed(IReadOnlyList<GltfImportDiagnostic>diagnostics)=>
        new(false,null,diagnostics);
}
