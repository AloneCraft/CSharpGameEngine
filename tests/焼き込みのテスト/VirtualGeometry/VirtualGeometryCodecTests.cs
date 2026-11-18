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
    /// <summary>版 2 は法線マップを書いて読む。版 1 のバイト列（法線マップの欄が無い）も、法線マップの無いものとして読める。</summary>
    [Fact]
    public void NormalMapsRoundTripAndVersionOneBlocksStillRead(){
        var Mesh=Build(VirtualGeometryBuilderTests.Box(4));
        var Pixels=Enumerable.Range(0,4*4*4).Select(static Index=>(byte)(Index*7)).ToArray();
        var WithMap=Mesh with{ NormalMapSize=4,NormalMap=Pixels };
        var Read=VirtualGeometryCodec.Read(VirtualGeometryCodec.Write([new(0,0,WithMap)]));
        Assert.Equal(4,Read[0].Mesh.NormalMapSize);
        Assert.Equal(Pixels,Read[0].Mesh.NormalMap);
        Assert.Throws<InvalidDataException>(()=>VirtualGeometryCodec.Write([new(0,0,Mesh with{ NormalMapSize=5,NormalMap=Pixels })]));
        // 版 1 は、版 2 から法線マップの大きさの欄(最後の 4 バイト)を除き、版の番号を 1 にしたもの。
        var VersionTwo=VirtualGeometryCodec.Write([new(0,0,Mesh)]);
        var VersionOne=VersionTwo.AsSpan(0,VersionTwo.Length-4).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(VersionOne.AsSpan(4),1);
        var Old=VirtualGeometryCodec.Read(VersionOne);
        Assert.Null(Old[0].Mesh.NormalMap);
        Assert.Equal(Mesh.Clusters,Old[0].Mesh.Clusters);
    }
    private static VirtualGeometryMesh Build((Content.VirtualGeometry.VirtualGeometryVertex[]Vertices,uint[]Indices)Source)=>VirtualGeometryBuilder.Build(Source.Vertices,Source.Indices);
}
