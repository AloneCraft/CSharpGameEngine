using Xunit;
using Xunit.Abstractions;
using System.Numerics;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Cooking.Tests.VirtualGeometry;
/// <summary>
/// 塊を置き場に置く形（M0131 単位 3）の GPU の量。塊ごとに頂点を写すので、隣の塊と共有する縁の頂点が重なり、頂点は 32 個の区画単位で持つ。
/// ビルドした球で、全部を置いた量が、メッシュ全体で頂点を共有する形の何倍かを測って記録し、上限を約束にする（読み込みの予算の見積もりに使う）。
/// </summary>
public sealed class VirtualGeometrySlotMemoryTests(ITestOutputHelper Output) {
    private const int VertexBytes=48;
    [Fact]
    public void TheSlotPoolStaysWithinASmallMultipleOfTheSharedMesh(){
        var (Vertices,Indices)=Sphere(128,256);
        var Mesh=VirtualGeometryBuilder.Build(Vertices,Indices);
        var Info=new StageVirtualGeometryCreateInfo {
            Vertices=[..Mesh.Vertices.Select(static Vertex=>new StageVirtualGeometryVertex { Position=Vertex.Position,Normal=Vertex.Normal,Tangent=Vertex.Tangent,Uv=Vertex.Uv })],
            Indices=Mesh.Indices,
            Clusters=[..Mesh.Clusters.Select(static Cluster=>new StageVirtualGeometryCluster { FirstIndex=(uint)Cluster.FirstIndex,IndexCount=(uint)Cluster.IndexCount,Group=(uint)Cluster.Group,ParentGroup=Cluster.ParentGroup })],
            Groups=[..Mesh.Groups.Select(static _=>new StageVirtualGeometryGroup())],
        };
        var Layout=VirtualGeometrySlotLayout.Create(Info);
        var Shared=(long)Info.Vertices.Length*VertexBytes+(long)Info.Indices.Length*sizeof(uint);
        var Pool=(long)Layout.TotalBlockCount*VirtualGeometrySlotLayout.VerticesPerBlock*VertexBytes+(long)Layout.ClusterCount*VirtualGeometrySlotLayout.IndicesPerSlot*sizeof(uint);
        var Average=Enumerable.Range(0,Layout.ClusterCount).Average(Layout.VertexCount);
        var Used=Enumerable.Range(0,Layout.ClusterCount).Sum(Cluster=>(long)Layout.VertexCount(Cluster)*VertexBytes+Info.Clusters[Cluster].IndexCount*sizeof(uint));
        Output.WriteLine($"{Indices.Length/3} source triangles, {Layout.ClusterCount} clusters in {Mesh.LevelCount} levels");
        Output.WriteLine($"largest cluster {Enumerable.Range(0,Layout.ClusterCount).Max(Layout.VertexCount)} vertices, average {Average:F1}, {Layout.TotalBlockCount} blocks of {VirtualGeometrySlotLayout.VerticesPerBlock}");
        Output.WriteLine($"shared mesh {Shared/1024.0:F0} KiB, slot pool {Pool/1024.0:F0} KiB ({(double)Pool/Shared:F2}x), bytes the clusters actually use {Used/1024.0:F0} KiB ({(double)Used/Shared:F2}x)");
        Assert.True(Pool*2<=Shared*5,$"the slot pool is {(double)Pool/Shared:F2} times the shared mesh");
    }
    private static (VirtualGeometryVertex[]Vertices,uint[]Indices)Sphere(int Rings,int Segments){
        List<VirtualGeometryVertex>Vertices=[];
        for(var Ring=0;Ring<=Rings;Ring++){
            var (SinPolar,CosPolar)=Ring==0?(0f,1f):Ring==Rings?(0f,-1f):MathF.SinCos(MathF.PI*Ring/Rings);
            for(var Segment=0;Segment<=Segments;Segment++){
                var (SinAzimuth,CosAzimuth)=Segment==Segments?(0f,1f):MathF.SinCos(2f*MathF.PI*Segment/Segments);
                var Normal=new Vector3(SinPolar*CosAzimuth,CosPolar,SinPolar*SinAzimuth);
                Vertices.Add(new VirtualGeometryVertex(Normal,Normal,new Vector4(1f,0f,0f,1f),new Vector2((float)Segment/Segments,(float)Ring/Rings)));
            }
        }
        List<uint>Indices=[];
        for(var Ring=0;Ring<Rings;Ring++){
            for(var Segment=0;Segment<Segments;Segment++){
                var A=(uint)(Ring*(Segments+1)+Segment);
                var B=A+(uint)Segments+1;
                if(Ring>0){ Indices.AddRange([A,A+1,B]); }
                if(Ring<Rings-1){ Indices.AddRange([A+1,B+1,B]); }
            }
        }
        return ([..Vertices],[..Indices]);
    }
}
