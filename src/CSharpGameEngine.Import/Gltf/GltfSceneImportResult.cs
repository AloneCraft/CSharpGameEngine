namespace CSharpGameEngine.Import.Gltf;

public sealed class GltfSceneImportResult {
    private GltfSceneImportResult(bool Success,ImportedScene?Scene,IReadOnlyList<GltfImportDiagnostic>Diagnostics){
        this.Success=Success;
        this.Scene=Scene;
        this.Diagnostics=Diagnostics;
    }

    public bool Success { get;}

    /// <summary>The imported scene. Always <see langword="null"/> when <see cref="Success"/> is false.</summary>
    public ImportedScene?Scene { get;}

    public IReadOnlyList<GltfImportDiagnostic>Diagnostics { get;}

    public static GltfSceneImportResult Succeeded(ImportedScene Scene,IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>
        new(true,Scene,Diagnostics);

    public static GltfSceneImportResult Failed(IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>
        new(false,null,Diagnostics);
}
