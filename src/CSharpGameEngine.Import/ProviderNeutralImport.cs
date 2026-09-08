using CSharpGameEngine.Content;

namespace CSharpGameEngine.Import;

/// <summary>The sole engine-owned boundary shared by provider-specific glTF and FBX adapters.</summary>
public static class ProviderNeutralImport {
    public static ContentMeshImportResult ImportGltf(string SourcePath){
        Gltf.GltfImportResult Result=Gltf.GltfImporter.Import(SourcePath);
        ContentImportDiagnostic[]Diagnostics=Result.Diagnostics
            .Select(static Diagnostic=>new ContentImportDiagnostic(
                Map(Diagnostic.Severity),"SharpGLTF","provider-import",Diagnostic.Message))
            .ToArray();
        if(!Result.Success||Result.Mesh is not { }Mesh){ return EnsureFailed(Diagnostics,"SharpGLTF"); }
        return TryConvert(
            "SharpGLTF",
            Mesh.Positions,Mesh.Normals,Mesh.Uvs,Mesh.Indices,
            Mesh.BaseColorFactor,Mesh.BaseColorTextureBytes,
            Diagnostics);
    }

    public static ContentMeshImportResult ImportFbx(string SourcePath){
        Fbx.FbxImportResult Result=Fbx.FbxImporter.Import(SourcePath);
        ContentImportDiagnostic[]Diagnostics=Result.Diagnostics
            .Select(static Diagnostic=>new ContentImportDiagnostic(
                Map(Diagnostic.Severity),"AssimpNet","provider-import",Diagnostic.Message))
            .ToArray();
        if(!Result.Success||Result.Mesh is not { }Mesh){ return EnsureFailed(Diagnostics,"AssimpNet"); }
        return TryConvert(
            "AssimpNet",
            Mesh.Positions,Mesh.Normals,Mesh.Uvs,Mesh.Indices,
            Mesh.BaseColorFactor,Mesh.BaseColorTextureBytes,
            Diagnostics);
    }

    private static ContentMeshImportResult TryConvert(
        string Provider,
        IEnumerable<System.Numerics.Vector3>Positions,
        IEnumerable<System.Numerics.Vector3>Normals,
        IEnumerable<System.Numerics.Vector2>TextureCoordinates,
        IEnumerable<uint>Indices,
        System.Numerics.Vector4 BaseColorFactor,
        byte[]?BaseColorTexture,
        IEnumerable<ContentImportDiagnostic>Diagnostics){
        try {
            return ContentMeshImportResult.Succeeded(
                ContentMeshIr.Create(Positions,Normals,TextureCoordinates,Indices),
                new ContentPbrMaterialIr(BaseColorFactor,BaseColorTexture),
                Diagnostics);
        }catch(ArgumentException Exception){
            return ContentMeshImportResult.Failed(
                Diagnostics.Append(new ContentImportDiagnostic(
                    ContentImportDiagnosticSeverity.Error,Provider,"engine-ir",Exception.Message)));
        }
    }

    private static ContentMeshImportResult EnsureFailed(
        IEnumerable<ContentImportDiagnostic>Diagnostics,string Provider){
        ContentImportDiagnostic[]Values=Diagnostics.ToArray();
        return Values.Any(static Diagnostic=>Diagnostic.Severity==ContentImportDiagnosticSeverity.Error)
            ?ContentMeshImportResult.Failed(Values)
            :ContentMeshImportResult.Failed(Values.Append(new ContentImportDiagnostic(
                ContentImportDiagnosticSeverity.Error,Provider,"provider-import","Provider import failed without an error diagnostic.")));
    }

    private static ContentImportDiagnosticSeverity Map(Gltf.GltfImportDiagnosticSeverity Severity)=>Severity switch {
        Gltf.GltfImportDiagnosticSeverity.Info=>ContentImportDiagnosticSeverity.Info,
        Gltf.GltfImportDiagnosticSeverity.Warning=>ContentImportDiagnosticSeverity.Warning,
        _=>ContentImportDiagnosticSeverity.Error,
    };
    private static ContentImportDiagnosticSeverity Map(Fbx.FbxImportDiagnosticSeverity Severity)=>Severity switch {
        Fbx.FbxImportDiagnosticSeverity.Info=>ContentImportDiagnosticSeverity.Info,
        Fbx.FbxImportDiagnosticSeverity.Warning=>ContentImportDiagnosticSeverity.Warning,
        _=>ContentImportDiagnosticSeverity.Error,
    };
}
