using System.Numerics;
using CSharpGameEngine.Import.Gltf;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
namespace CSharpGameEngine.Import.Tests.Gltf;
/// <summary>ADR-0066：スキンとアニメーションはメッシュ形状と整合するよう、インポーターからエンジン空間のまま出力される。</summary>
public sealed class GltfSkinAnimationImportTests {
    private static readonly Quaternion QuarterTurnAboutX=Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI/2f);
    /// <summary>2 つのジョイント。軸変換が分かるように子を glTF の +Z 方向へオフセットし、子を +X 周りに回転させるクリップ。</summary>
    private static string SaveSkinnedFixture(){
        var Root=new NodeBuilder("root");
        var Child=Root.CreateNode("child").WithLocalTranslation(new Vector3(0f,1f,0.5f));
        var Rotation=Child.UseRotation("Bend");
        Rotation.SetPoint(0f,Quaternion.Identity);
        Rotation.SetPoint(1f,QuarterTurnAboutX);
        var Mesh=new MeshBuilder<VertexPosition,VertexEmpty,VertexJoints4>("skinned");
        var Primitive=Mesh.UsePrimitive(new MaterialBuilder("mat").WithMetallicRoughness());
        Primitive.AddTriangle(
            (new VertexPosition(0f,0f,0f),new VertexJoints4((0,1f))),
            (new VertexPosition(0f,2f,0.5f),new VertexJoints4((1,1f))),
            (new VertexPosition(1f,2f,0.5f),new VertexJoints4((0,1f),(1,3f))));
        var Scene=new SceneBuilder();
        Scene.AddSkinnedMesh(Mesh,Matrix4x4.Identity,Root,Child);
        var Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),$"gltf-skin-test-{Guid.NewGuid():N}.glb");
        Scene.ToGltf2().SaveGLB(Path);
        return Path;
    }
    private static void AssertNear(Matrix4x4 Expected,Matrix4x4 Actual){
        for(var Row=0;Row<4;Row++){
            for(var Column=0;Column<4;Column++){
                Assert.InRange(Actual[Row,Column],Expected[Row,Column]-1e-4f,Expected[Row,Column]+1e-4f);
            }
        }
    }
    [Fact]
    public void ConvertRotation_MatchesTheWorldTransformMirror(){
        var Rotation=Quaternion.Normalize(new Quaternion(0.3f,-0.5f,0.2f,0.78f));
        var Converted=GltfAxisConversion.ConvertWorldTransform(Matrix4x4.CreateFromQuaternion(Rotation));
        AssertNear(Converted,Matrix4x4.CreateFromQuaternion(GltfAxisConversion.ConvertRotation(Rotation)));
    }
    [Fact]
    public void ImportScene_ReadsTheSkinInEngineSpace(){
        var Path=SaveSkinnedFixture();
        try {
            var Result=GltfImporter.ImportScene(Path);
            Assert.True(Result.Success,string.Join(";",Result.Diagnostics));
            var Scene=Result.Scene!;
            var Skin=Assert.Single(Scene.Skins);
            Assert.Equal(["root","child"],Skin.JointNames);
            Assert.Equal([-1,0],Skin.ParentIndices);
            Assert.Equal(new Vector3(0f,1f,-0.5f),Skin.RestTranslations[1]);
            // 静止時、すべてのパレット要素は単位行列になる。逆バインド行列が静止モデル変換を取り消すため。
            var Model=new Matrix4x4[2];
            for(var Joint=0;Joint<2;Joint++){
                var Local=Matrix4x4.CreateScale(Skin.RestScales[Joint])*Matrix4x4.CreateFromQuaternion(Skin.RestRotations[Joint])*Matrix4x4.CreateTranslation(Skin.RestTranslations[Joint]);
                Model[Joint]=Local*(Skin.ParentIndices[Joint]<0?Skin.SkeletonTransform:Model[Skin.ParentIndices[Joint]]);
                AssertNear(Matrix4x4.Identity,Skin.InverseBindMatrices[Joint]*Model[Joint]);
            }
            var Node=Assert.Single(Scene.Nodes);
            Assert.Equal(0,Node.SkinIndex);
            var Mesh=Scene.Meshes[Node.MeshIndex!.Value];
            var Influences=Mesh.Influences!;
            const int Slots=ImportedSkinInfluences.InfluencesPerVertex;
            Assert.Equal(Mesh.Positions.Count*Slots,Influences.Weights.Count);
            for(var Vertex=0;Vertex<Mesh.Positions.Count;Vertex++){
                var Sum=0f;
                for(var Slot=0;Slot<Slots;Slot++){ Sum+=Influences.Weights[(Vertex*Slots)+Slot]; }
                Assert.InRange(Sum,0.999f,1.001f);
            }
            // 混合頂点（x = 1 でエンジン z = -0.5）は、ジョイント 0 の 3 倍の重みをジョイント 1 に与える。
            var Mixed=Enumerable.Range(0,Mesh.Positions.Count).Single(Vertex=>Mesh.Positions[Vertex].X>0.5f);
            Assert.Equal(new Vector3(1f,2f,-0.5f),Mesh.Positions[Mixed]);
            var WeightOfChild=Enumerable.Range(0,Slots).Where(Slot=>Influences.JointIndices[(Mixed*Slots)+Slot]==1).Sum(Slot=>Influences.Weights[(Mixed*Slots)+Slot]);
            Assert.InRange(WeightOfChild,0.749f,0.751f);
        }
        finally {
            File.Delete(Path);
        }
    }
    [Fact]
    public void ImportAnimations_ConvertsKeysToEngineSpace(){
        var Path=SaveSkinnedFixture();
        try {
            var Result=GltfImporter.ImportAnimations(Path);
            Assert.True(Result.Success,string.Join(";",Result.Diagnostics));
            var Clip=Assert.Single(Result.Clips);
            Assert.Equal("Bend",Clip.Name);
            Assert.Equal(1f,Clip.DurationSeconds);
            var Track=Assert.Single(Clip.Tracks);
            Assert.Equal("child",Track.JointName);
            Assert.Null(Track.Translation);
            var Keys=Track.Rotation!;
            Assert.False(Keys.Step);
            Assert.Equal([0f,1f],Keys.Times);
            var Last=Keys.Values[^1];
            var Expected=GltfAxisConversion.ConvertRotation(QuarterTurnAboutX);
            Assert.InRange(MathF.Abs(Quaternion.Dot(Expected,Last)),0.9999f,1.0001f);
            Assert.True(Last.X<0f,$"rotation {Last}");
        }
        finally {
            File.Delete(Path);
        }
    }
}
