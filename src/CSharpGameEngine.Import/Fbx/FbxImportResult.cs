namespace CSharpGameEngine.Import.Fbx;

public sealed class FbxImportResult {
    private FbxImportResult(bool Success,ImportedMesh?Mesh,IReadOnlyList<FbxImportDiagnostic>Diagnostics){
        this.Success=Success;
        this.Mesh=Mesh;
        this.Diagnostics=Diagnostics;
    }

    public bool Success { get;}

    /// <summary>インポートしたメッシュ。<see cref="Success"/>がfalseの場合は常に<see langword="null"/>。</summary>
    public ImportedMesh?Mesh { get;}

    public IReadOnlyList<FbxImportDiagnostic>Diagnostics { get;}

    public static FbxImportResult Succeeded(ImportedMesh Mesh,IReadOnlyList<FbxImportDiagnostic>Diagnostics)=>
        new(true,Mesh,Diagnostics);

    public static FbxImportResult Failed(IReadOnlyList<FbxImportDiagnostic>Diagnostics)=>
        new(false,null,Diagnostics);
}
