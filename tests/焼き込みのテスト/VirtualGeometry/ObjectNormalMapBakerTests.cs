using Xunit;
using System.Numerics;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
namespace CSharpGameEngine.Cooking.Tests.VirtualGeometry;
/// <summary>物体の向きの法線マップを焼く(M0130 単位 6)。UV で塗った画素は頂点の法線をつないだ向き、UV が重なれば焼かない、大きさは三角形の数で決まる。</summary>
public sealed class ObjectNormalMapBakerTests {
    [Fact]
    public void ASphereBakesItsSmoothNormalsAtTheirUvs(){
        var (Vertices,Indices)=VirtualGeometryBuilderTests.Sphere(32,64);
        const int Size=256;
        var Pixels=ObjectNormalMapBaker.Bake(Vertices,Indices,Size);
        Assert.NotNull(Pixels);
        Assert.Equal(Size*Size*4,Pixels.Length);
        // 継ぎ目と極から離れた頂点の UV の画素で、焼いた向きが頂点の法線と一致する。
        foreach(var Vertex in Vertices.Where(static Vertex=>Vertex.Uv.X is >0.05f and <0.95f&&Vertex.Uv.Y is >0.1f and <0.9f).Take(40)){
            var X=Math.Clamp((int)(Vertex.Uv.X*Size),0,Size-1);
            var Y=Math.Clamp((int)(Vertex.Uv.Y*Size),0,Size-1);
            var Offset=(Y*Size+X)*4;
            var Baked=new Vector3(Pixels[Offset],Pixels[Offset+1],Pixels[Offset+2])/255f*2f-Vector3.One;
            Assert.True(Vector3.Dot(Vector3.Normalize(Baked),Vertex.Normal)>0.98f,$"uv {Vertex.Uv}: baked {Baked}, vertex {Vertex.Normal}");
        }
    }
    [Fact]
    public void OverlappingUvsAreNotBaked(){
        var (Vertices,Indices)=VirtualGeometryBuilderTests.Sphere(16,32);
        // UV を左右で折り返す(左右対称のテクスチャでよくある形)。
        var Mirrored=Vertices.Select(static Vertex=>Vertex with{ Uv=new Vector2(MathF.Abs(Vertex.Uv.X-0.5f)*2f,Vertex.Uv.Y) }).ToArray();
        Assert.Null(ObjectNormalMapBaker.Bake(Mirrored,Indices,128));
        var Flat=Vertices.Select(static Vertex=>Vertex with{ Uv=Vector2.Zero }).ToArray();
        Assert.Null(ObjectNormalMapBaker.Bake(Flat,Indices,128));
    }
    [Theory]
    [InlineData(1,256)]
    [InlineData(4096,256)]
    [InlineData(65024,1024)]
    [InlineData(10000000,2048)]
    public void TheSizeFollowsTheTriangleCount(int Triangles,int Expected)=>Assert.Equal(Expected,ObjectNormalMapBaker.SizeFor(Triangles));
}
