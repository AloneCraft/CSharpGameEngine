using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace CSharpGameEngine.Import.Gltf.Tests;

/// <summary>
/// ImportScene builds a whole node graph (unlike Import(), which only ever reads one primitive),
/// so these fixtures are built in-memory with SharpGLTF.Toolkit rather than committed .glb files -
/// each test needs a specific, otherwise-hard-to-obtain shape (nested transforms, a light node, a
/// primitive with no TANGENT accessor).
/// </summary>
public class GltfImporterSceneTests
{
    private static MeshBuilder<VertexPosition, VertexTexture1, VertexEmpty> BuildTriangleMesh(string name, MaterialBuilder material)
    {
        var mesh = new MeshBuilder<VertexPosition, VertexTexture1, VertexEmpty>(name);
        var primitive = mesh.UsePrimitive(material);
        primitive.AddTriangle(
            (new VertexPosition(0, 0, 0), new VertexTexture1(new Vector2(0, 0))),
            (new VertexPosition(1, 0, 0), new VertexTexture1(new Vector2(1, 0))),
            (new VertexPosition(0, 1, 0), new VertexTexture1(new Vector2(0, 1))));
        return mesh;
    }

    private static string SaveToTempGlb(SceneBuilder sceneBuilder)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gltf-scene-test-{Guid.NewGuid():N}.glb");
        sceneBuilder.ToGltf2().SaveGLB(path);
        return path;
    }

    [Fact]
    public void ImportScene_NestedNodes_ComposesWorldTransformThroughParentChain()
    {
        var mesh = BuildTriangleMesh("probe", new MaterialBuilder("mat").WithMetallicRoughness());

        var parent = new NodeBuilder("Parent");
        parent.LocalTransform = new AffineTransform(Matrix4x4.CreateTranslation(10, 0, 0));
        NodeBuilder child = parent.CreateNode("Child");
        child.LocalTransform = new AffineTransform(Matrix4x4.CreateTranslation(0, 5, 0));

        var sceneBuilder = new SceneBuilder();
        sceneBuilder.AddRigidMesh(mesh, child);

        string path = SaveToTempGlb(sceneBuilder);
        try
        {
            GltfSceneImportResult result = GltfImporter.ImportScene(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            ImportedSceneNode node = Assert.Single(result.Scene!.Nodes);
            Assert.Equal("Child", node.Name);

            // World translation must be the composed parent(10,0,0) * child(0,5,0) = (10,5,0), not
            // just the child's own local offset - this is the whole point of using WorldMatrix.
            Assert.Equal(new Vector3(10, 5, 0), node.WorldTransform.Translation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportScene_MultipleMeshInstances_ShareOneMeshEntryPerLogicalMesh()
    {
        var mesh = BuildTriangleMesh("shared", new MaterialBuilder("mat").WithMetallicRoughness());

        var sceneBuilder = new SceneBuilder();
        sceneBuilder.AddRigidMesh(mesh, new NodeBuilder("First"));
        sceneBuilder.AddRigidMesh(mesh, new NodeBuilder("Second") { LocalTransform = new AffineTransform(Matrix4x4.CreateTranslation(3, 0, 0)) });

        string path = SaveToTempGlb(sceneBuilder);
        try
        {
            GltfSceneImportResult result = GltfImporter.ImportScene(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            ImportedScene scene = result.Scene!;
            Assert.Equal(2, scene.Nodes.Count);
            Assert.Single(scene.Meshes);
            Assert.All(scene.Nodes, n => Assert.Equal(0, n.MeshIndex));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportScene_PrimitiveWithoutTangentAccessor_GeneratesUnitTangentsOrthogonalToNormal()
    {
        var mesh = BuildTriangleMesh("noTangent", new MaterialBuilder("mat").WithMetallicRoughness());

        var sceneBuilder = new SceneBuilder();
        sceneBuilder.AddRigidMesh(mesh, new NodeBuilder("Flat"));

        string path = SaveToTempGlb(sceneBuilder);
        try
        {
            GltfSceneImportResult result = GltfImporter.ImportScene(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            ImportedMeshData meshData = Assert.Single(result.Scene!.Meshes);

            Assert.Equal(meshData.Positions.Count, meshData.Tangents.Count);
            for (int i = 0; i < meshData.Positions.Count; i++)
            {
                Vector3 normal = meshData.Normals[i];
                Vector4 tangent4 = meshData.Tangents[i];
                var tangent = new Vector3(tangent4.X, tangent4.Y, tangent4.Z);

                Assert.Equal(1.0f, tangent.Length(), precision: 3);
                Assert.Equal(0.0f, Vector3.Dot(normal, tangent), precision: 3);
                Assert.True(tangent4.W is 1f or -1f);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportScene_DirectionalLightNode_ImportsColorIntensityAndWorldDirection()
    {
        var sceneBuilder = new SceneBuilder();
        var light = new LightBuilder.Directional { Color = new Vector3(1, 0, 0), Intensity = 6 };
        // A light pointing straight down: rotate -90 degrees around +X so local -Z (the fixed
        // KHR_lights_punctual direction) maps to world -Y.
        var lightNode = new NodeBuilder("Sun") { LocalTransform = new AffineTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f)) };
        sceneBuilder.AddLight(light, lightNode);

        string path = SaveToTempGlb(sceneBuilder);
        try
        {
            GltfSceneImportResult result = GltfImporter.ImportScene(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            ImportedSceneNode node = Assert.Single(result.Scene!.Nodes);
            Assert.NotNull(node.LightIndex);
            Assert.Null(node.MeshIndex);

            ImportedLight importedLight = result.Scene.Lights[node.LightIndex!.Value];
            Assert.Equal(ImportedLightType.Directional, importedLight.LightType);
            Assert.Equal(6f, importedLight.Intensity, precision: 3);
            Assert.Equal(new Vector3(1, 0, 0), importedLight.Color);

            Vector3 worldDirection = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, node.WorldTransform));
            Assert.Equal(new Vector3(0, -1, 0), worldDirection, EqualityComparerFor(0.001f));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportScene_FileDoesNotExist_ReturnsFailedWithError()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "does-not-exist-scene.glb");

        GltfSceneImportResult result = GltfImporter.ImportScene(missingPath);

        Assert.False(result.Success);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, d => d.Severity == GltfImportDiagnosticSeverity.Error && d.Message.Contains(missingPath));
    }

    private static IEqualityComparer<Vector3> EqualityComparerFor(float tolerance) =>
        new ApproximateVector3Comparer(tolerance);

    private sealed class ApproximateVector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;

        public int GetHashCode(Vector3 obj) => 0;
    }
}
