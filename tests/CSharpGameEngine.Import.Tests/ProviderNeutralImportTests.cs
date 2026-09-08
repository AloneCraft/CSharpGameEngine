using CSharpGameEngine.Content;

namespace CSharpGameEngine.Import.Tests;

public sealed class ProviderNeutralImportTests {
    [Fact]
    public void GltfAndFbxConvergeOnTheSameEngineOwnedMeshContract(){
        ContentMeshImportResult Gltf=ProviderNeutralImport.ImportGltf(FixturePath("Box.glb"));
        ContentMeshImportResult Fbx=ProviderNeutralImport.ImportFbx(FixturePath("Box.fbx"));

        Assert.True(Gltf.Success,string.Join(Environment.NewLine,Gltf.Diagnostics));
        Assert.True(Fbx.Success,string.Join(Environment.NewLine,Fbx.Diagnostics));
        Assert.NotNull(Gltf.Mesh);
        Assert.NotNull(Fbx.Mesh);
        Assert.NotEmpty(Gltf.Mesh.Positions);
        Assert.NotEmpty(Fbx.Mesh.Positions);
        Assert.Equal(0,Gltf.Mesh.Indices.Length%3);
        Assert.Equal(0,Fbx.Mesh.Indices.Length%3);
        Assert.Equal(typeof(ContentMeshIr),Gltf.Mesh.GetType());
        Assert.Equal(typeof(ContentMeshIr),Fbx.Mesh.GetType());
        Assert.DoesNotContain(
            Gltf.Mesh.GetType().GetProperties(),
            static Property=>Property.PropertyType.Namespace?.StartsWith("SharpGLTF",StringComparison.Ordinal)==true);
        Assert.DoesNotContain(
            Fbx.Mesh.GetType().GetProperties(),
            static Property=>Property.PropertyType.Namespace?.StartsWith("Assimp",StringComparison.Ordinal)==true);
    }

    [Fact]
    public void MissingSourceReturnsProviderNeutralDiagnostics(){
        ContentMeshImportResult Result=ProviderNeutralImport.ImportGltf(Path.Combine(
            Path.GetTempPath(),Guid.NewGuid().ToString("N")+".glb"));

        Assert.False(Result.Success);
        ContentImportDiagnostic Diagnostic=Assert.Single(
            Result.Diagnostics.Where(static Diagnostic=>Diagnostic.Severity==ContentImportDiagnosticSeverity.Error));
        Assert.Equal("SharpGLTF",Diagnostic.Provider);
    }

    private static string FixturePath(string FileName)=>
        Path.Combine(AppContext.BaseDirectory,"Fixtures",FileName);
}
