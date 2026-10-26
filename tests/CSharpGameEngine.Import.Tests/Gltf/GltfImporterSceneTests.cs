using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace CSharpGameEngine.Import.Gltf.Tests;

/// <summary>
/// ImportScene はノードグラフ全体を構築する（Import() はプリミティブを 1 つだけ読み取る）。そのため、これらのフィクスチャは .glb ファイルとしてコミットせず、SharpGLTF.Toolkit でメモリ上に構築する。各テストに、入れ子の変換、ライトノード、TANGENT アクセサーのないプリミティブなど、個別で通常は入手しにくい形状が必要となるため。
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

            // ワールド位置は親 (10,0,0) * 子 (0,5,0) を合成した (10,5,0) でなければならず、子の
            // ローカルオフセットそのものではない。これが WorldMatrix を使う理由である。
            Assert.Equal(new Vector3(10,5,0),Node.WorldTransform.Translation);
        } finally {
            File.Delete(Path);
        }
    }

    /// <summary>
    /// ADR-0027 の回帰ガード：上の入れ子変換テストでは Z=0 のフィクスチャだけを使うため、軸変換が壊れているか欠落していても検出できない。どちらの場合も数値が変わらないためである。ここでは Z を 0 以外にし、変換の実際の効果（Z の符号反転）を証明する。
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

    /// <summary>ADR-0027 の回帰ガード：ノード変換ではなく、メッシュローカルの頂点データを対象とする。</summary>
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

            // ソース頂点は glTF 空間の (0,1,2)。エンジン空間では Z の符号を反転して (0,1,-2) になる。
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
        // 真下を向くライト：+X 周りに -90 度回転し、ローカル -Z（固定された
        // KHR_lights_punctual の方向）をワールド -Y に対応させる。
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
            var Directional=Assert.IsType<ImportedDirectionalLight>(ImportedLight);
            Assert.Equal(6f,Directional.Illuminance.Lux,precision:3);
            Assert.Equal(new CSharpGameEngine.Lighting.LinearLightColor(1,0,0),ImportedLight.Color);

            // node.WorldTransform はすでにエンジン空間である (ADR-0027)。そこでのローカル Forward は +Z であり、
            // glTF 自身のローカル -Z ではない。そのため方向は ImportedLightDirection.WorldForward から取得する。
            // 手書きの -Vector3.UnitZ 変換ではなく、ImportedLightDirection.WorldForward を使う。
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
    [Fact]
    public void ImportScene_PointAndSpot_PreserveCandelaConeAndOptionalRange(){
        var Scene=new SceneBuilder();
        Scene.AddLight(new LightBuilder.Point { Color=Vector3.One,Intensity=25f },new NodeBuilder("Point"));
        Scene.AddLight(new LightBuilder.Spot { Color=Vector3.One,Intensity=50f,Range=12f,InnerConeAngle=0.2f,OuterConeAngle=0.5f },new NodeBuilder("Spot"));
        var Path=SaveToTempGlb(Scene);
        try{
            var Result=GltfImporter.ImportScene(Path);
            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            var Point=Assert.Single(Result.Scene!.Lights.OfType<ImportedPointLight>());
            Assert.Equal(25f,Point.Intensity.Candela);
            Assert.Null(Point.Range);
            var Spot=Assert.Single(Result.Scene.Lights.OfType<ImportedSpotLight>());
            Assert.Equal(50f,Spot.Intensity.Candela);
            Assert.Equal(12f,Spot.Range!.Value.Meters);
            Assert.Equal(0.2f,Spot.Cone.InnerHalfAngleRadians);
            Assert.Equal(0.5f,Spot.Cone.OuterHalfAngleRadians);
            var Cone=Spot.Cone.ToAuthoredCone();
            var Flux=CSharpGameEngine.Lighting.LightPhotometry.SpotFlux(Spot.Intensity,Cone);
            Assert.Equal(50f,CSharpGameEngine.Lighting.LightPhotometry.SpotIntensity(Flux,Cone).Candela,3);
        }finally{ File.Delete(Path); }
    }
    [Fact]
    public void ImportedZeroInnerCone_IsPreservedButNeedsExplicitAuthoredResolution(){
        var Cone=new ImportedSpotCone(0f,0.5f);
        Assert.Equal(0f,Cone.InnerHalfAngleRadians);
        Assert.Throws<ArgumentOutOfRangeException>(()=>Cone.ToAuthoredCone());
    }
}
