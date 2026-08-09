namespace CSharpGameEngine.Import.Fbx.Tests;

public class FbxImporterTests
{
    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public void Import_BoxFbx_Succeeds_WithPositionsNormalsAndIndices()
    {
        FbxImportResult result = FbxImporter.Import(FixturePath("Box.fbx"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Mesh);
        Assert.Equal(result.Mesh.Positions.Count, result.Mesh.Normals.Count);
        Assert.Equal(result.Mesh.Positions.Count, result.Mesh.Uvs.Count);
        Assert.True(result.Mesh.Indices.Count > 0);
        Assert.Equal(0, result.Mesh.Indices.Count % 3);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == FbxImportDiagnosticSeverity.Error);

        foreach (uint index in result.Mesh.Indices)
        {
            Assert.True(index < result.Mesh.Positions.Count);
        }
    }

    [Fact]
    public void Import_BoxFbx_BoundingBox_IsPlausible()
    {
        FbxImportResult result = FbxImporter.Import(FixturePath("Box.fbx"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        BoundingBox bounds = result.Mesh!.Bounds;

        Assert.True(bounds.Min.X < bounds.Max.X);
        Assert.True(bounds.Min.Y < bounds.Max.Y);
        Assert.True(bounds.Min.Z < bounds.Max.Z);
        Assert.InRange(bounds.BoundingSphereRadius, 0.01f, 100f);
    }

    [Fact]
    public void Import_FileDoesNotExist_ReturnsFailedWithError()
    {
        string missingPath = FixturePath("DoesNotExist.fbx");

        FbxImportResult result = FbxImporter.Import(missingPath);

        Assert.False(result.Success);
        Assert.Null(result.Mesh);
        Assert.Contains(result.Diagnostics, d => d.Severity == FbxImportDiagnosticSeverity.Error && d.Message.Contains(missingPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_NullOrWhitespacePath_Throws(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => FbxImporter.Import(path!));
    }

    [Fact]
    public void Import_MalformedFile_ReturnsFailedNotException()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"malformed-{Guid.NewGuid():N}.fbx");
        File.WriteAllText(tempPath, "this is not a valid FBX file");

        try
        {
            FbxImportResult result = FbxImporter.Import(tempPath);

            Assert.False(result.Success);
            Assert.Null(result.Mesh);
            Assert.Contains(result.Diagnostics, d => d.Severity == FbxImportDiagnosticSeverity.Error);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
