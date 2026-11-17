using Xunit;
using System.Buffers.Binary;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
namespace CSharpGameEngine.Cooking.Tests.VirtualGeometry;
/// <summary>仮想ジオメトリの読み書き(M0130 単位 3)。書いて読んで同じ、同じ入力から同じバイト列、壊れたデータを断る。</summary>
public sealed class VirtualGeometryCodecTests {
    [Fact]
    public void WritingAndReadingGivesTheSameMeshesAndTheSameBytes(){
        var Sphere=Build(VirtualGeometryBuilderTests.Sphere(24,48));
        var Box=Build(VirtualGeometryBuilderTests.Box(8));
        VirtualGeometryEntry[]Entries=[new(2,1,Box),new(0,0,Sphere)];
        var Bytes=VirtualGeometryCodec.Write(Entries);
        var Read=VirtualGeometryCodec.Read(Bytes);
        Assert.Equal([(0,0),(2,1)],Read.Select(static Entry=>(Entry.MeshIndex,Entry.SubmeshIndex)));
        foreach(var (Expected,Actual) in new[]{ (Sphere,Read[0].Mesh),(Box,Read[1].Mesh) }){
            Assert.Equal(Expected.Vertices,Actual.Vertices);
            Assert.Equal(Expected.Indices,Actual.Indices);
            Assert.Equal(Expected.Clusters,Actual.Clusters);
            Assert.Equal(Expected.Groups,Actual.Groups);
            Assert.Equal((Expected.RootGroup,Expected.LevelCount),(Actual.RootGroup,Actual.LevelCount));
        }
        Assert.Equal(Bytes,VirtualGeometryCodec.Write([..Read]));
    }
    [Fact]
    public void BrokenBlocksAreRefused(){
        var Mesh=Build(VirtualGeometryBuilderTests.Box(4));
        var Bytes=VirtualGeometryCodec.Write([new(0,0,Mesh)]);
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Read(Bytes.AsSpan(0,Bytes.Length-1)));
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Read([..Bytes,0]));
        var BadMagic=(byte[])Bytes.Clone();
        BadMagic[0]^=1;
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Read(BadMagic));
        // 最初の頂点の番号を、頂点の数の外にする。
        var BadIndex=(byte[])Bytes.Clone();
        var FirstIndexOffset=4+2+4+8*4+Mesh.Vertices.Length*48;
        BinaryPrimitives.WriteUInt32LittleEndian(BadIndex.AsSpan(FirstIndexOffset),(uint)Mesh.Vertices.Length);
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Read(BadIndex));
        // 最初の頂点の x を NaN にする。
        var BadNumber=(byte[])Bytes.Clone();
        BinaryPrimitives.WriteSingleLittleEndian(BadNumber.AsSpan(4+2+4+8*4),float.NaN);
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Read(BadNumber));
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Write([new(0,0,Mesh),new(0,0,Mesh)]));
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Write([new(0,0,Mesh with{ RootGroup=Mesh.Groups.Length })]));
    }
    [Fact]
    public void TheProxyIsTheRootLevelAndCoarserThanTheSource(){
        var Mesh=Build(VirtualGeometryBuilderTests.Sphere(32,64));
        var Proxy=Mesh.ProxyIndices();
        Assert.True(Proxy.Length>0&&Proxy.Length%3==0);
        Assert.True(Proxy.Length*8<32*64*2*3,$"the proxy keeps {Proxy.Length/3} triangles");
    }
    private static VirtualGeometryMesh Build((Content.VirtualGeometry.VirtualGeometryVertex[]Vertices,uint[]Indices)Source)=>VirtualGeometryBuilder.Build(Source.Vertices,Source.Indices);
}
