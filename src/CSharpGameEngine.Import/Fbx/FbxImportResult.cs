namespace CSharpGameEngine.Import.Fbx;

public sealed class FbxImportResult {
    private FbxImportResult(bool success,ImportedMesh?mesh,IReadOnlyList<FbxImportDiagnostic>diagnostics){
        Success=success;
        Mesh=mesh;
        Diagnostics=diagnostics;
    }

    public bool Success { get;}

    /// <summary>The imported mesh. Always <see langword="null"/> when <see cref="Success"/> is false.</summary>
    public ImportedMesh?Mesh { get;}

    public IReadOnlyList<FbxImportDiagnostic>Diagnostics { get;}

    public static FbxImportResult Succeeded(ImportedMesh mesh,IReadOnlyList<FbxImportDiagnostic>diagnostics)=>
        new(true,mesh,diagnostics);

    public static FbxImportResult Failed(IReadOnlyList<FbxImportDiagnostic>diagnostics)=>
        new(false,null,diagnostics);
}
