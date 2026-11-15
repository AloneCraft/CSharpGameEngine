namespace CSharpGameEngine.Import.Gltf;

public sealed class GltfSceneImportResult {
    private GltfSceneImportResult(bool Success,ImportedScene?Scene,IReadOnlyList<GltfImportDiagnostic>Diagnostics){
        this.Success=Success;
        this.Scene=Scene;
        this.Diagnostics=Diagnostics;
    }

    public bool Success { get;}

    /// <summary>インポートしたシーン。<see cref="Success"/>がfalseの場合は常に<see langword="null"/>。</summary>
    public ImportedScene?Scene { get;}

    public IReadOnlyList<GltfImportDiagnostic>Diagnostics { get;}

    public static GltfSceneImportResult Succeeded(ImportedScene Scene,IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>
        new(true,Scene,Diagnostics);

    public static GltfSceneImportResult Failed(IReadOnlyList<GltfImportDiagnostic>Diagnostics)=>
        new(false,null,Diagnostics);
}
