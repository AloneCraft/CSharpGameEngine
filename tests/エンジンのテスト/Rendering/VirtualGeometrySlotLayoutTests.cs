using Xunit;
using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>
/// 塊を置き場へ置く形（M0131 単位 3）。索引の枠と頂点の区画をどこに置いても、頂点シェーダーと同じ引き方（塊の FirstIndex から索引、索引から頂点）で、
/// 元のメッシュと同じ三角形が出る。区画は頂点の数に要るだけで、使わない索引は最後の索引で埋まる（面積 0）。
/// </summary>
public sealed class VirtualGeometrySlotLayoutTests {
    [Fact]
    public void ScatteredSlotsAndBlocksDrawTheSameTrianglesAsTheSourceMesh(){
        var Info=Grid(9,7,TrianglesPerCluster:20);
        var Layout=VirtualGeometrySlotLayout.Create(Info);
        Assert.Equal(Info.Clusters.Length,Layout.ClusterCount);
        Assert.Equal(Enumerable.Range(0,Layout.ClusterCount).Sum(Layout.BlockCount),Layout.TotalBlockCount);
        // 読み込みで置き場の空いた所へ置く場合のように、枠と区画を逆順・とびとびに置いても同じ三角形になる。
        var SlotCount=Layout.ClusterCount+3;
        var BlockCount=Layout.TotalBlockCount*2+1;
        var Vertices=new StageVirtualGeometryVertex[BlockCount*VirtualGeometrySlotLayout.VerticesPerBlock];
        var Indices=new uint[SlotCount*VirtualGeometrySlotLayout.IndicesPerSlot];
        var NextBlock=BlockCount-1;
        for(var Cluster=0;Cluster<Layout.ClusterCount;Cluster++){
            var Slot=SlotCount-1-Cluster;
            var Blocks=new int[Layout.BlockCount(Cluster)];
            for(var Block=0;Block<Blocks.Length;Block++){
                Blocks[Block]=NextBlock;
                NextBlock-=2;
                Layout.WriteBlock(Cluster,Block,Vertices.AsSpan(Blocks[Block]*VirtualGeometrySlotLayout.VerticesPerBlock,VirtualGeometrySlotLayout.VerticesPerBlock));
            }
            Layout.WriteIndices(Cluster,Blocks,Indices.AsSpan(Slot*VirtualGeometrySlotLayout.IndicesPerSlot,VirtualGeometrySlotLayout.IndicesPerSlot));
            var Placed=Layout.SlotCluster(Cluster,Slot);
            var Source=Info.Clusters[Cluster];
            Assert.Equal((uint)(Slot*VirtualGeometrySlotLayout.IndicesPerSlot),Placed.FirstIndex);
            Assert.Equal((Source.IndexCount,Source.Group,Source.ParentGroup),(Placed.IndexCount,Placed.Group,Placed.ParentGroup));
            for(var Corner=0u;Corner<VirtualGeometrySlotLayout.IndicesPerSlot;Corner++){
                // 頂点シェーダーと同じく、索引の数より後ろは最後の角として引く。
                var Clamped=Math.Min(Corner,Source.IndexCount-1);
                var Expected=Info.Vertices[Info.Indices[Source.FirstIndex+Clamped]];
                var Pool=Indices[Placed.FirstIndex+Corner];
                var Actual=Vertices[Pool];
                Assert.Equal(Expected.Position,Actual.Position);
                Assert.Equal(Expected.Normal,Actual.Normal);
                Assert.Equal(Expected.Uv,Actual.Uv);
                // 索引は自分の区画の頂点だけを指す。
                Assert.Contains((int)(Pool/VirtualGeometrySlotLayout.VerticesPerBlock),Blocks);
            }
        }
    }
    [Fact]
    public void ASharedVertexIsCopiedIntoEachClusterThatUsesIt(){
        var Info=Grid(3,1,TrianglesPerCluster:2);
        var Layout=VirtualGeometrySlotLayout.Create(Info);
        // 3×1 マスの格子: 8 頂点、6 三角形、3 塊。隣り合う塊は縁の 2 頂点を共有し、それぞれの塊が自分の写しを持つ。
        Assert.Equal(3,Layout.ClusterCount);
        Assert.All(Enumerable.Range(0,3),Cluster=>Assert.Equal(4,Layout.VertexCount(Cluster)));
        Assert.All(Enumerable.Range(0,3),Cluster=>Assert.Equal(1,Layout.BlockCount(Cluster)));
    }
    [Fact]
    public void AClusterWithManyVerticesUsesSeveralBlocks(){
        // 1 塊 128 三角形の格子（8×8 マス、81 頂点）は 3 区画を使う。
        var Info=Grid(8,8,TrianglesPerCluster:128);
        var Layout=VirtualGeometrySlotLayout.Create(Info);
        Assert.Equal(81,Layout.VertexCount(0));
        Assert.Equal(3,Layout.BlockCount(0));
    }
    [Fact]
    public void AClusterOutsideItsMeshIsRejected(){
        var Info=Grid(2,2,TrianglesPerCluster:4);
        var Broken=Info with{ Indices=[..Info.Indices.Select((Index,Position)=>Position==0?999u:Index)] };
        Assert.Throws<ArgumentException>(()=>VirtualGeometrySlotLayout.Create(Broken));
    }
    /// <summary>X×Y マスの平らな格子。三角形を <paramref name="TrianglesPerCluster"/> 枚ずつ順に塊にする。</summary>
    private static StageVirtualGeometryCreateInfo Grid(int X,int Y,int TrianglesPerCluster){
        List<StageVirtualGeometryVertex>Vertices=[];
        for(var Row=0;Row<=Y;Row++){
            for(var Column=0;Column<=X;Column++){
                Vertices.Add(new StageVirtualGeometryVertex { Position=new Vector3(Column,0f,Row),Normal=Vector3.UnitY,Tangent=new Vector4(1f,0f,0f,1f),Uv=new Vector2((float)Column/X,(float)Row/Y) });
            }
        }
        List<uint>Indices=[];
        for(var Row=0;Row<Y;Row++){
            for(var Column=0;Column<X;Column++){
                var A=(uint)(Row*(X+1)+Column);
                var B=A+(uint)(X+1);
                Indices.AddRange([A,B,A+1,A+1,B,B+1]);
            }
        }
        List<StageVirtualGeometryCluster>Clusters=[];
        for(var First=0;First<Indices.Count;First+=TrianglesPerCluster*3){
            Clusters.Add(new StageVirtualGeometryCluster { FirstIndex=(uint)First,IndexCount=(uint)Math.Min(TrianglesPerCluster*3,Indices.Count-First),Group=(uint)Clusters.Count,ParentGroup=-1 });
        }
        return new StageVirtualGeometryCreateInfo {
            Vertices=[..Vertices],
            Indices=[..Indices],
            Clusters=[..Clusters],
            Groups=[..Clusters.Select(static _=>new StageVirtualGeometryGroup())],
        };
    }
}
