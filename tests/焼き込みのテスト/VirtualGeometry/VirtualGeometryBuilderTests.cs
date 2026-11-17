using Xunit;
using System.Numerics;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Cooking.Tests.VirtualGeometry;
/// <summary>
/// 塊の階層のビルド(M0130 単位 2、ADR-0100 決定 3)。割れ目が出ない(どの選び方でも、選んだ塊の三角形で閉じた形が閉じたまま)、誤差は親で 1.25 倍以上、
/// 球は親が子を包む、塊は 128 三角形以下、元の三角形はいちばん細かい段にすべて残る、同じ入力で同じ結果。
/// </summary>
public sealed class VirtualGeometryBuilderTests {
    [Fact]
    public void ASphereBuildsSeveralLevelsWithBoundedClustersAndMonotonicErrorsAndBounds(){
        var (Vertices,Indices)=Sphere(48,96);
        var Mesh=VirtualGeometryBuilder.Build(Vertices,Indices);
        Assert.True(Mesh.LevelCount>=4,$"{Mesh.LevelCount} levels");
        Assert.All(Mesh.Clusters,Cluster=>Assert.InRange(Cluster.IndexCount/3,1,128));
        var LeafTriangles=Mesh.Clusters.Where(Cluster=>Mesh.Groups[Cluster.Group].Level==0).Sum(static Cluster=>Cluster.IndexCount/3);
        Assert.Equal(Indices.Length/3,LeafTriangles);
        foreach(var Cluster in Mesh.Clusters){
            if(Cluster.ParentGroup<0){
                Assert.Equal(Mesh.RootGroup,Cluster.Group);
                continue;
            }
            var Own=Mesh.Groups[Cluster.Group];
            var Parent=Mesh.Groups[Cluster.ParentGroup];
            Assert.True(Parent.Error>=Own.Error*VirtualGeometryBuilder.MinimumErrorGrowth*0.9999f,$"parent error {Parent.Error} < 1.25 x {Own.Error}");
            Assert.True(Vector3.Distance(Parent.BoundsCentre,Own.BoundsCentre)+Own.BoundsRadius<=Parent.BoundsRadius*1.0001f+1e-6f,"a parent sphere does not enclose its child's");
        }
        var RootTriangles=Mesh.Clusters.Where(Cluster=>Cluster.ParentGroup<0).Sum(static Cluster=>Cluster.IndexCount/3);
        Assert.True(RootTriangles*8<Indices.Length/3,$"the root keeps {RootTriangles} of {Indices.Length/3} triangles");
    }
    /// <summary>誤差のしきい値をいくつに選んでも、選んだ塊の三角形でできた形は閉じている(どの辺もちょうど 2 枚の三角形が使う)。穴も重なりも無い。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryErrorThresholdSelectsAWatertightSurface(bool HardEdgedBox){
        var (Vertices,Indices)=HardEdgedBox?Box(24):Sphere(40,80);
        var Mesh=VirtualGeometryBuilder.Build(Vertices,Indices);
        var Errors=Mesh.Groups.Select(static Group=>Group.Error).Distinct().Order().ToArray();
        foreach(var Threshold in Errors.Append(0f).Append(float.MaxValue/4f)){
            var Selected=Mesh.Clusters.Where(Cluster=>Mesh.Groups[Cluster.Group].Error<=Threshold&&(Cluster.ParentGroup<0||Mesh.Groups[Cluster.ParentGroup].Error>Threshold)).ToArray();
            AssertWatertight(Mesh,Selected,$"threshold {Threshold}");
        }
    }
    /// <summary>カメラから見た画素の誤差で選んでも閉じている。画面上の大きさの式は親で子以上になる(親の球が子を包み、誤差も大きい)ので、距離で細かさが変わっても割れない。</summary>
    [Fact]
    public void ScreenSpaceSelectionFromAnyCameraIsWatertight(){
        var (Vertices,Indices)=Sphere(40,80);
        var Mesh=VirtualGeometryBuilder.Build(Vertices,Indices);
        var Metric=ScreenSizeMetric.Perspective(1f,1080,0.01f);
        foreach(var Camera in new[]{ new Vector3(0f,0f,1.2f),new Vector3(3f,0.5f,0f),new Vector3(0f,-20f,0f),new Vector3(0.9f,0.9f,0.9f) }){
            foreach(var Pixels in new[]{ 0.5f,1f,4f,16f }){
                float Projected(int Group){
                    var G=Mesh.Groups[Group];
                    var Distance=MathF.Max((G.BoundsCentre-Camera).Length()-G.BoundsRadius,Metric.MinimumDistanceMeters);
                    return Metric.ProjectedPixels(G.Error,Distance);
                }
                var Selected=Mesh.Clusters.Where(Cluster=>Projected(Cluster.Group)<=Pixels&&(Cluster.ParentGroup<0||Projected(Cluster.ParentGroup)>Pixels)).ToArray();
                AssertWatertight(Mesh,Selected,$"camera {Camera}, {Pixels} px");
            }
        }
    }
    [Fact]
    public void TheSameInputGivesTheSameResult(){
        var (Vertices,Indices)=Sphere(32,64);
        var First=VirtualGeometryBuilder.Build(Vertices,Indices);
        var Second=VirtualGeometryBuilder.Build(Vertices,Indices);
        Assert.Equal(First.Indices,Second.Indices);
        Assert.Equal(First.Clusters,Second.Clusters);
        Assert.Equal(First.Groups,Second.Groups);
        Assert.Equal(First.Vertices,Second.Vertices);
        Assert.Equal(First.RootGroup,Second.RootGroup);
    }
    [Fact]
    public void ASmallMeshIsItsOwnRoot(){
        var (Vertices,Indices)=Box(1);
        var Mesh=VirtualGeometryBuilder.Build(Vertices,Indices);
        Assert.Single(Mesh.Clusters);
        Assert.Equal(1,Mesh.LevelCount);
        Assert.Equal(-1,Mesh.Clusters[0].ParentGroup);
    }
    [Fact]
    public void InvalidInputIsRefused(){
        var (Vertices,Indices)=Box(1);
        Assert.Throws<ArgumentException>(()=>VirtualGeometryBuilder.Build(Vertices,Indices.AsSpan(0,5)));
        Assert.Throws<ArgumentException>(()=>VirtualGeometryBuilder.Build(Vertices,new uint[]{ 0,1,999 }));
        Assert.Throws<ArgumentOutOfRangeException>(()=>VirtualGeometryBuilder.Build(Vertices,Indices,new VirtualGeometryBuildSettings { MaxTrianglesPerCluster=129 }));
        var Bad=(VirtualGeometryVertex[])Vertices.Clone();
        Bad[0]=Bad[0] with{ Position=new Vector3(float.NaN,0f,0f) };
        Assert.Throws<ArgumentException>(()=>VirtualGeometryBuilder.Build(Bad,Indices));
    }
    /// <summary>位置で見て、どの辺もちょうど 2 枚の三角形が使う(閉じた入力の形が、選んだ塊でも閉じている)。</summary>
    private static void AssertWatertight(VirtualGeometryMesh Mesh,VirtualGeometryCluster[]Selected,string Case){
        var Ids=new Dictionary<Vector3,int>();
        int Id(uint Vertex){
            var Position=Mesh.Vertices[Vertex].Position;
            if(!Ids.TryGetValue(Position,out var Value)){
                Value=Ids.Count;
                Ids.Add(Position,Value);
            }
            return Value;
        }
        var Edges=new Dictionary<(int,int),int>();
        foreach(var Cluster in Selected){
            for(var Corner=0;Corner<Cluster.IndexCount;Corner+=3){
                for(var Side=0;Side<3;Side++){
                    int A=Id(Mesh.Indices[Cluster.FirstIndex+Corner+Side]),B=Id(Mesh.Indices[Cluster.FirstIndex+Corner+(Side+1)%3]);
                    var Key=A<B?(A,B):(B,A);
                    Edges[Key]=Edges.GetValueOrDefault(Key)+1;
                }
            }
        }
        var Open=Edges.Count(static Edge=>Edge.Value!=2);
        Assert.True(Open==0,$"{Case}: {Open} of {Edges.Count} edges are not shared by exactly two triangles ({Selected.Length} clusters)");
    }
    internal static (VirtualGeometryVertex[]Vertices,uint[]Indices)Sphere(int Rings,int Segments){
        var (Simplifier,Indices)=QuadricSimplifierTests.Sphere(Rings,Segments);
        return ([..Simplifier.Select(static Vertex=>new VirtualGeometryVertex(Vertex.Position,Vertex.Normal,new Vector4(1f,0f,0f,1f),Vertex.Uv))],Indices);
    }
    internal static (VirtualGeometryVertex[]Vertices,uint[]Indices)Box(int Subdivisions){
        var (Simplifier,Indices)=QuadricSimplifierTests.SubdividedBox(Subdivisions);
        return ([..Simplifier.Select(static Vertex=>new VirtualGeometryVertex(Vertex.Position,Vertex.Normal,new Vector4(1f,0f,0f,1f),Vertex.Uv))],Indices);
    }
}
