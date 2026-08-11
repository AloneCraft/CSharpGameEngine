namespace CSharpGameEngine.Import.Fbx.Tests;

public class FbxImporterTests
{
    private static string FixturePath(string FileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", FileName);

    [Fact]
    public void Import_BoxFbx_Succeeds_WithPositionsNormalsAndIndices()
    {
        FbxImportResult Result = FbxImporter.Import(FixturePath("Box.fbx"));

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        Assert.NotNull(Result.Mesh);
        Assert.Equal(Result.Mesh.Positions.Count, Result.Mesh.Normals.Count);
        Assert.Equal(Result.Mesh.Positions.Count, Result.Mesh.Uvs.Count);
        Assert.True(Result.Mesh.Indices.Count > 0);
        Assert.Equal(0, Result.Mesh.Indices.Count % 3);
        Assert.DoesNotContain(Result.Diagnostics, D => D.Severity == FbxImportDiagnosticSeverity.Error);

        foreach (uint index in Result.Mesh.Indices)
        {
            Assert.True(index < Result.Mesh.Positions.Count);
        }
    }

    [Fact]
    public void Import_BoxFbx_BoundingBox_IsPlausible()
    {
        FbxImportResult Result = FbxImporter.Import(FixturePath("Box.fbx"));

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        BoundingBox Bounds = Result.Mesh!.Bounds;

        Assert.True(Bounds.Min.X < Bounds.Max.X);
        Assert.True(Bounds.Min.Y < Bounds.Max.Y);
        Assert.True(Bounds.Min.Z < Bounds.Max.Z);
        Assert.InRange(Bounds.BoundingSphereRadius, 0.01f, 100f);
    }

    [Fact]
    public void Import_FileDoesNotExist_ReturnsFailedWithError()
    {
        string MissingPath = FixturePath("DoesNotExist.fbx");

        FbxImportResult Result = FbxImporter.Import(MissingPath);

        Assert.False(Result.Success);
        Assert.Null(Result.Mesh);
        Assert.Contains(Result.Diagnostics, D => D.Severity == FbxImportDiagnosticSeverity.Error && D.Message.Contains(MissingPath));
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
        string TempPath = Path.Combine(Path.GetTempPath(), $"malformed-{Guid.NewGuid():N}.fbx");
        File.WriteAllText(TempPath, "this is not a valid FBX file");

        try
        {
            FbxImportResult Result = FbxImporter.Import(TempPath);

            Assert.False(Result.Success);
            Assert.Null(Result.Mesh);
            Assert.Contains(Result.Diagnostics, D => D.Severity == FbxImportDiagnosticSeverity.Error);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }
}
