namespace CSharpGameEngine.Import.Gltf;

public sealed class GltfImportResult {
    private GltfImportResult(bool Success,ImportedMesh?Mesh,IReadOnlyList<GltfImportDiagnostic>Diagnostics){
        this.Success=Success;
        this.Mesh=Mesh;
        this.Diagnostics=Diagnostics;
    }

    public bool Success { get;}

    /// <summary>インポートしたメッシュ。<see cref="Success"/>がfalseの場合は常に<see langword="null"/>。</summary>
    public ImportedMesh?Mesh { get;}

    public IReadOnlyList<GltfImportDiagnostic>Diagnostics { get;}

    public static GltfImportResult Succeeded(ImportedMesh Mesh,IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>
        new(true,Mesh,Diagnostics);

    public static GltfImportResult Failed(IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>
        new(false,null,Diagnostics);
}
