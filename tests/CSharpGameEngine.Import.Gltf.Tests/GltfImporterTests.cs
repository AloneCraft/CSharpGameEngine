using System.Numerics;

namespace CSharpGameEngine.Import.Gltf.Tests;

public class GltfImporterTests
{
    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public void Import_BoxGlb_Succeeds_WithPositionsNormalsAndIndices()
    {
        GltfImportResult result = GltfImporter.Import(FixturePath("Box.glb"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Mesh);
        Assert.Equal(result.Mesh.Positions.Count, result.Mesh.Normals.Count);
        Assert.True(result.Mesh.Indices.Count > 0);
        Assert.Equal(0, result.Mesh.Indices.Count % 3);
        // Box.glb has no TEXCOORD_0, so a UV-synthesis Warning is expected; no Errors either way.
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Warning && d.Message.Contains("TEXCOORD_0"));

        foreach (uint index in result.Mesh.Indices)
        {
            Assert.True(index < result.Mesh.Positions.Count);
        }
    }

    [Fact]
    public void Import_BoxGlb_HasSynthesizedUvsNoTextureAndRedFactor()
    {
        GltfImportResult result = GltfImporter.Import(FixturePath("Box.glb"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var mesh = result.Mesh!;

        Assert.Equal(mesh.Positions.Count, mesh.Uvs.Count);
        Assert.All(mesh.Uvs, uv => Assert.Equal(Vector2.Zero, uv));
        Assert.Null(mesh.BaseColorTextureBytes);

        // Confirmed by parsing the real Box.glb JSON: pbrMetallicRoughness.baseColorFactor == [0.8, 0, 0, 1].
        Assert.Equal(0.8f, mesh.BaseColorFactor.X, precision: 2);
        Assert.Equal(0f, mesh.BaseColorFactor.Y, precision: 2);
        Assert.Equal(0f, mesh.BaseColorFactor.Z, precision: 2);
        Assert.Equal(1f, mesh.BaseColorFactor.W, precision: 2);
    }

    [Fact]
    public void Import_BoxGlb_BoundingBox_IsPlausible()
    {
        GltfImportResult result = GltfImporter.Import(FixturePath("Box.glb"));

        Assert.True(result.Success);
        BoundingBox bounds = result.Mesh!.Bounds;

        Assert.True(bounds.Min.X < bounds.Max.X);
        Assert.True(bounds.Min.Y < bounds.Max.Y);
        Assert.True(bounds.Min.Z < bounds.Max.Z);
        Assert.True(bounds.BoundingSphereRadius > 0f);
        // The Khronos "Box" sample is a small unit-ish cube; assert it's a plausible small object
        // rather than a brittle exact literal.
        Assert.InRange(bounds.BoundingSphereRadius, 0.01f, 100f);
    }

    [Fact]
    public void Import_TriangleGltf_MissingNormal_SynthesizesFlatNormalsWithWarning()
    {
        GltfImportResult result = GltfImporter.Import(FixturePath("Triangle.gltf"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Mesh);
        Assert.Contains(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Warning && d.Message.Contains("NORMAL"));
        Assert.Contains(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Warning && d.Message.Contains("TEXCOORD_0"));

        // Missing normals trigger full triangle expansion: 1 triangle -> 3 unshared vertices.
        Assert.Equal(3, result.Mesh.Positions.Count);
        Assert.Equal(3, result.Mesh.Normals.Count);
        Assert.Equal(3, result.Mesh.Uvs.Count);
        Assert.Equal(3, result.Mesh.Indices.Count);
        Assert.All(result.Mesh.Uvs, uv => Assert.Equal(Vector2.Zero, uv));
        Assert.Null(result.Mesh.BaseColorTextureBytes);
        Assert.Equal(new Vector4(1, 1, 1, 1), result.Mesh.BaseColorFactor);

        foreach (System.Numerics.Vector3 normal in result.Mesh.Normals)
        {
            Assert.Equal(1.0f, normal.Length(), precision: 3);
        }

        // A single triangle has one flat face normal shared by all 3 synthesized vertices.
        Assert.Equal(result.Mesh.Normals[0], result.Mesh.Normals[1]);
        Assert.Equal(result.Mesh.Normals[0], result.Mesh.Normals[2]);
    }

    [Fact]
    public void Import_BoxTexturedGlb_Succeeds_WithTextureBytesUvsAndDefaultFactor()
    {
        GltfImportResult result = GltfImporter.Import(FixturePath("BoxTextured.glb"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var mesh = result.Mesh!;

        // NORMAL and TEXCOORD_0 are both present in this fixture, so no expansion happens.
        Assert.Equal(mesh.Positions.Count, mesh.Uvs.Count);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity is GltfImportDiagnosticSeverity.Error or GltfImportDiagnosticSeverity.Warning);

        Assert.NotNull(mesh.BaseColorTextureBytes);
        Assert.True(mesh.BaseColorTextureBytes!.Length > 0);
        // PNG magic number: 0x89 'P' 'N' 'G'.
        Assert.Equal(0x89, mesh.BaseColorTextureBytes[0]);
        Assert.Equal((byte)'P', mesh.BaseColorTextureBytes[1]);
        Assert.Equal((byte)'N', mesh.BaseColorTextureBytes[2]);
        Assert.Equal((byte)'G', mesh.BaseColorTextureBytes[3]);

        // Confirmed by parsing the real BoxTextured.glb JSON: no explicit baseColorFactor -> glTF default (1,1,1,1).
        Assert.Equal(new Vector4(1, 1, 1, 1), mesh.BaseColorFactor);
    }

    [Fact]
    public void Import_FileDoesNotExist_ReturnsFailedWithError()
    {
        string missingPath = FixturePath("DoesNotExist.glb");

        GltfImportResult result = GltfImporter.Import(missingPath);

        Assert.False(result.Success);
        Assert.Null(result.Mesh);
        Assert.Contains(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Error && d.Message.Contains(missingPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_NullOrWhitespacePath_Throws(string? path)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException specifically for
        // null, and plain ArgumentException for empty/whitespace - both are acceptable here.
        Assert.ThrowsAny<ArgumentException>(() => GltfImporter.Import(path!));
    }

    [Fact]
    public void Import_MalformedFile_ReturnsFailedNotException()
    {
        GltfImportResult result = GltfImporter.Import(FixturePath("Malformed.glb"));

        Assert.False(result.Success);
        Assert.Null(result.Mesh);
        Assert.Contains(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Error);
    }
}
