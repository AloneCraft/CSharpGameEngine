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
public class GltfImporterSceneTests {
    private static MeshBuilder<VertexPosition,VertexTexture1,VertexEmpty>BuildTriangleMesh(string Name,MaterialBuilder Material){
        var Mesh=new MeshBuilder<VertexPosition,VertexTexture1,VertexEmpty>(Name);
        var Primitive=Mesh.UsePrimitive(Material);
        Primitive.AddTriangle(
            (new VertexPosition(0,0,0),new VertexTexture1(new Vector2(0,0))),
            (new VertexPosition(1,0,0),new VertexTexture1(new Vector2(1,0))),
            (new VertexPosition(0,1,0),new VertexTexture1(new Vector2(0,1))));
        return Mesh;
    }

    private static string SaveToTempGlb(SceneBuilder SceneBuilder){
        var Path= System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gltf-scene-test-{Guid.NewGuid():N}.glb");
        SceneBuilder.ToGltf2().SaveGLB(Path);
        return Path;
    }

    [Fact]
    public void ImportScene_NestedNodes_ComposesWorldTransformThroughParentChain(){
        var Mesh=BuildTriangleMesh("probe",new MaterialBuilder("mat").WithMetallicRoughness());

        var Parent=new NodeBuilder("Parent");
        Parent.LocalTransform=new AffineTransform(Matrix4x4.CreateTranslation(10,0,0));
        var Child=Parent.CreateNode("Child");
        Child.LocalTransform=new AffineTransform(Matrix4x4.CreateTranslation(0,5,0));

        var SceneBuilder=new SceneBuilder();
        SceneBuilder.AddRigidMesh(Mesh,Child);

        var Path=SaveToTempGlb(SceneBuilder);
        try {
            var Result=GltfImporter.ImportScene(Path);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var Node=Assert.Single(Result.Scene!.Nodes);
            Assert.Equal("Child",Node.Name);

            // World translation must be the composed parent(10,0,0) * child(0,5,0) = (10,5,0), not
            // just the child's own local offset - this is the whole point of using WorldMatrix.
            Assert.Equal(new Vector3(10,5,0),Node.WorldTransform.Translation);
        } finally {
            File.Delete(Path);
        }
    }

    /// <summary>
    /// ADR-0027 regression guard: the nested-transform test above uses Z=0 fixtures throughout, so a
    /// broken (or missing) axis conversion would go completely undetected there - the numbers happen
    /// not to change either way. This uses a non-zero Z so the conversion's actual effect (Z negation)
    /// is provable.
    /// </summary>
    [Fact]
    public void ImportScene_NodeWithZTranslation_NegatesZWhenConvertingToEngineSpace(){
        var Mesh=BuildTriangleMesh("probe",new MaterialBuilder("mat").WithMetallicRoughness());

        var Node=new NodeBuilder("ZOffset");
        Node.LocalTransform=new AffineTransform(Matrix4x4.CreateTranslation(0,0,5));

        var SceneBuilder=new SceneBuilder();
        SceneBuilder.AddRigidMesh(Mesh,Node);

        var Path=SaveToTempGlb(SceneBuilder);
        try {
            var Result=GltfImporter.ImportScene(Path);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var ImportedNode=Assert.Single(Result.Scene!.Nodes);

            Assert.Equal(new Vector3(0,0,-5),ImportedNode.WorldTransform.Translation);
        } finally {
            File.Delete(Path);
        }
    }

    /// <summary>ADR-0027 regression guard for mesh-local vertex data (as opposed to the node transform above).</summary>
    [Fact]
    public void ImportScene_MeshLocalPositions_AreConvertedToEngineSpace(){
        var Mesh=new MeshBuilder<VertexPosition,VertexTexture1,VertexEmpty>("zProbe");
        var Primitive=Mesh.UsePrimitive(new MaterialBuilder("mat").WithMetallicRoughness());
        Primitive.AddTriangle(
            (new VertexPosition(0,0,0),new VertexTexture1(new Vector2(0,0))),
            (new VertexPosition(1,0,0),new VertexTexture1(new Vector2(1,0))),
            (new VertexPosition(0,1,2),new VertexTexture1(new Vector2(0,1))));

        var SceneBuilder=new SceneBuilder();
        SceneBuilder.AddRigidMesh(Mesh,new NodeBuilder("ZProbeNode"));

        var Path=SaveToTempGlb(SceneBuilder);
        try {
            var Result=GltfImporter.ImportScene(Path);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var MeshData=Assert.Single(Result.Scene!.Meshes);

            // Source vertex was glTF-space (0,1,2); engine space must have Z negated to (0,1,-2).
            Assert.Contains(MeshData.Positions,P=>Vector3.Distance(P,new Vector3(0,1,-2))<0.001f);
        } finally {
            File.Delete(Path);
        }
    }

    [Fact]
    public void ImportScene_MultipleMeshInstances_ShareOneMeshEntryPerLogicalMesh(){
        var Mesh=BuildTriangleMesh("shared",new MaterialBuilder("mat").WithMetallicRoughness());

        var SceneBuilder=new SceneBuilder();
        SceneBuilder.AddRigidMesh(Mesh,new NodeBuilder("First"));
        SceneBuilder.AddRigidMesh(Mesh,new NodeBuilder("Second"){ LocalTransform=new AffineTransform(Matrix4x4.CreateTranslation(3,0,0))});

        var Path=SaveToTempGlb(SceneBuilder);
        try {
            var Result=GltfImporter.ImportScene(Path);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var Scene=Result.Scene!;
            Assert.Equal(2,Scene.Nodes.Count);
            Assert.Single(Scene.Meshes);
            Assert.All(Scene.Nodes,N=>Assert.Equal(0,N.MeshIndex));
        } finally {
            File.Delete(Path);
        }
    }

    [Fact]
    public void ImportScene_PrimitiveWithoutTangentAccessor_GeneratesUnitTangentsOrthogonalToNormal(){
        var Mesh=BuildTriangleMesh("noTangent",new MaterialBuilder("mat").WithMetallicRoughness());

        var SceneBuilder=new SceneBuilder();
        SceneBuilder.AddRigidMesh(Mesh,new NodeBuilder("Flat"));

        var Path=SaveToTempGlb(SceneBuilder);
        try {
            var Result=GltfImporter.ImportScene(Path);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var MeshData=Assert.Single(Result.Scene!.Meshes);

            Assert.Equal(MeshData.Positions.Count,MeshData.Tangents.Count);
            for(var I=0;I<MeshData.Positions.Count;I++){
                var Normal=MeshData.Normals[I];
                var Tangent4=MeshData.Tangents[I];
                var Tangent=new Vector3(Tangent4.X,Tangent4.Y,Tangent4.Z);

                Assert.Equal(1.0f,Tangent.Length(),precision:3);
                Assert.Equal(0.0f,Vector3.Dot(Normal,Tangent),precision:3);
                Assert.True(Tangent4.W is 1f or -1f);
            }
        } finally {
            File.Delete(Path);
        }
    }

    [Fact]
    public void ImportScene_DirectionalLightNode_ImportsColorIntensityAndWorldDirection(){
        var SceneBuilder=new SceneBuilder();
        var Light=new LightBuilder.Directional { Color=new Vector3(1,0,0),Intensity=6 };
        // A light pointing straight down: rotate -90 degrees around +X so local -Z (the fixed
        // KHR_lights_punctual direction) maps to world -Y.
        var LightNode=new NodeBuilder("Sun"){ LocalTransform=new AffineTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitX,-MathF.PI/2f))};
        SceneBuilder.AddLight(Light,LightNode);

        var Path=SaveToTempGlb(SceneBuilder);
        try {
            var Result=GltfImporter.ImportScene(Path);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var Node=Assert.Single(Result.Scene!.Nodes);
            Assert.NotNull(Node.LightIndex);
            Assert.Null(Node.MeshIndex);

            var ImportedLight=Result.Scene.Lights[Node.LightIndex!.Value];
            Assert.Equal(ImportedLightType.Directional,ImportedLight.LightType);
            Assert.Equal(6f,ImportedLight.Intensity,precision:3);
            Assert.Equal(new Vector3(1,0,0),ImportedLight.Color);

            // node.WorldTransform is already engine-space (ADR-0027): local Forward there is +Z, not
            // glTF's own local -Z, so the direction must come from ImportedLightDirection.WorldForward
            // rather than a hand-rolled -Vector3.UnitZ transform.
            var WorldDirection=ImportedLightDirection.WorldForward(Node.WorldTransform);
            Assert.Equal(new Vector3(0,-1,0),WorldDirection,EqualityComparerFor(0.001f));
        } finally {
            File.Delete(Path);
        }
    }

    [Fact]
    public void ImportScene_FileDoesNotExist_ReturnsFailedWithError(){
        var MissingPath=Path.Combine(Path.GetTempPath(),"does-not-exist-scene.glb");

        var Result=GltfImporter.ImportScene(MissingPath);

        Assert.False(Result.Success);
        Assert.Null(Result.Scene);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==GltfImportDiagnosticSeverity.Error&&D.Message.Contains(MissingPath));
    }

    private static IEqualityComparer<Vector3>EqualityComparerFor(float Tolerance)=>
        new ApproximateVector3Comparer(Tolerance);

    private sealed class ApproximateVector3Comparer(float Tolerance):IEqualityComparer<Vector3>{
        public bool Equals(Vector3 X,Vector3 Y)=>Vector3.Distance(X,Y)<=Tolerance;

        public int GetHashCode(Vector3 Obj)=>0;
    }
}
