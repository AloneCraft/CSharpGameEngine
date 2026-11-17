using Xunit;
using System.Numerics;
using CSharpGameEngine.Cooking.VirtualGeometry;
namespace CSharpGameEngine.Cooking.Tests.VirtualGeometry;
/// <summary>
/// QEM の簡略化(M0130 単位 1、ADR-0100 決定 3)。固定した頂点は残る、誤差の上限を守る、目標の数まで減る、継ぎ目の法線を壊さない、裏返りを作らない、同じ入力で同じ結果。
/// </summary>
public sealed class QuadricSimplifierTests {
    [Fact]
    public void AFlatGridWithAFixedBorderShrinksToFewTrianglesWithoutErrorAndKeepsEveryBorderVertex(){
        var (Vertices,Indices)=Grid(16);
        var Border=BorderOf(Vertices,16);
        var Result=QuadricSimplifier.Simplify(Vertices,Indices,Border,TargetTriangleCount:8);
        Assert.True(Result.Indices.Length/3<Indices.Length/3/4,$"{Result.Indices.Length/3} triangles remain of {Indices.Length/3}");
        Assert.Equal(0f,Result.Error,5);
        var Remaining=new HashSet<uint>(Result.Indices);
        Assert.All(Border,Index=>Assert.Contains(Index,Remaining));
        AssertNoFlip(Vertices,Result.Indices,Vector3.UnitY);
    }
    [Fact]
    public void ASphereReachesItsTargetAndAStricterErrorLimitStopsEarlier(){
        var (Vertices,Indices)=Sphere(24,48);
        var Target=Indices.Length/3/2;
        var Free=QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>(),Target);
        Assert.True(Free.Indices.Length/3<=Target,$"{Free.Indices.Length/3} > {Target}");
        Assert.True(Free.Error>0f);
        var Limited=QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>(),Target,MaximumError:Free.Error*0.25f);
        Assert.True(Limited.Error<=Free.Error*0.25f,$"{Limited.Error} exceeds the limit {Free.Error*0.25f}");
        Assert.True(Limited.Indices.Length>Free.Indices.Length);
        AssertOutward(Vertices,Free.Indices);
    }
    /// <summary>
    /// 面ごとに法線の違う頂点を持つ(角が継ぎ目の)、細かく分けた箱は、誤差 0 のまま面ごとに 2 枚、計 12 枚まで減る。どの三角形も、3 つの頂点の法線がその面の向きと同じ。
    /// </summary>
    [Fact]
    public void AHardEdgedSubdividedBoxReducesToTwelveTrianglesAndKeepsEachFaceNormal(){
        var (Vertices,Indices)=SubdividedBox(6);
        var Result=QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>(),TargetTriangleCount:1,MaximumError:1e-4f);
        Assert.Equal(12,Result.Indices.Length/3);
        for(var Corner=0;Corner<Result.Indices.Length;Corner+=3){
            var A=Vertices[(int)Result.Indices[Corner]];
            var B=Vertices[(int)Result.Indices[Corner+1]];
            var C=Vertices[(int)Result.Indices[Corner+2]];
            var Face=Vector3.Normalize(Vector3.Cross(B.Position-A.Position,C.Position-A.Position));
            foreach(var Vertex in new[]{ A,B,C }){ Assert.True(Vector3.Dot(Face,Vertex.Normal)>0.999f,$"normal {Vertex.Normal} on a face facing {Face}"); }
        }
    }
    [Fact]
    public void TheSameInputGivesTheSameResult(){
        var (Vertices,Indices)=Sphere(16,32);
        var First=QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>{ 0,5,9 },Indices.Length/3/3);
        var Second=QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>{ 0,5,9 },Indices.Length/3/3);
        Assert.Equal(First.Indices,Second.Indices);
        Assert.Equal(First.Error,Second.Error);
    }
    [Fact]
    public void InvalidInputIsRefused(){
        var (Vertices,Indices)=Grid(2);
        Assert.Throws<ArgumentException>(()=>QuadricSimplifier.Simplify(Vertices,Indices.AsSpan(0,4),new HashSet<uint>(),1));
        Assert.Throws<ArgumentException>(()=>QuadricSimplifier.Simplify(Vertices,new uint[]{ 0,1,999 },new HashSet<uint>(),1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>(),0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>QuadricSimplifier.Simplify(Vertices,Indices,new HashSet<uint>(),1,float.NaN));
    }
    /// <summary>xz 平面の (N+1) × (N+1) の格子。法線は +y。</summary>
    internal static (SimplifierVertex[]Vertices,uint[]Indices)Grid(int N){
        var Vertices=new SimplifierVertex[(N+1)*(N+1)];
        for(var Z=0;Z<=N;Z++){
            for(var X=0;X<=N;X++){ Vertices[Z*(N+1)+X]=new(new Vector3(X,0f,Z),Vector3.UnitY,new Vector2((float)X/N,(float)Z/N)); }
        }
        List<uint>Indices=[];
        for(var Z=0;Z<N;Z++){
            for(var X=0;X<N;X++){
                uint A=(uint)(Z*(N+1)+X),B=A+1,C=A+(uint)(N+1),D=C+1;
                Indices.AddRange([A,C,B,B,C,D]);
            }
        }
        return (Vertices,[..Indices]);
    }
    private static HashSet<uint>BorderOf(SimplifierVertex[]Vertices,int N){
        HashSet<uint>Border=[];
        for(var Index=0;Index<Vertices.Length;Index++){
            var P=Vertices[Index].Position;
            if(P.X==0f||P.Z==0f||P.X==N||P.Z==N){ Border.Add((uint)Index); }
        }
        return Border;
    }
    /// <summary>緯度・経度で分けた単位球。経度の継ぎ目では UV の違う頂点が同じ位置に重なる。</summary>
    internal static (SimplifierVertex[]Vertices,uint[]Indices)Sphere(int Rings,int Segments){
        List<SimplifierVertex>Vertices=[];
        for(var Ring=0;Ring<=Rings;Ring++){
            var Theta=MathF.PI*Ring/Rings;
            for(var Segment=0;Segment<=Segments;Segment++){
                var Phi=2f*MathF.PI*Segment/Segments;
                var Normal=new Vector3(MathF.Sin(Theta)*MathF.Cos(Phi),MathF.Cos(Theta),MathF.Sin(Theta)*MathF.Sin(Phi));
                Vertices.Add(new(Normal,Normal,new Vector2((float)Segment/Segments,(float)Ring/Rings)));
            }
        }
        List<uint>Indices=[];
        for(var Ring=0;Ring<Rings;Ring++){
            for(var Segment=0;Segment<Segments;Segment++){
                uint A=(uint)(Ring*(Segments+1)+Segment),B=A+1,C=A+(uint)(Segments+1),D=C+1;
                if(Ring!=0){ Indices.AddRange([A,B,C]); }
                if(Ring!=Rings-1){ Indices.AddRange([B,D,C]); }
            }
        }
        return ([..Vertices],[..Indices]);
    }
    /// <summary>1 辺 2 の箱の 6 面を、それぞれ N × N に分ける。面ごとに頂点を持つので、角と辺は法線の継ぎ目になる。</summary>
    internal static (SimplifierVertex[]Vertices,uint[]Indices)SubdividedBox(int N){
        List<SimplifierVertex>Vertices=[];
        List<uint>Indices=[];
        foreach(var Normal in new[]{ Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ }){
            var U=MathF.Abs(Normal.Y)>0.5f?Vector3.UnitX:Vector3.UnitY;
            var V=Vector3.Cross(Normal,U);
            var First=(uint)Vertices.Count;
            for(var J=0;J<=N;J++){
                for(var I=0;I<=N;I++){
                    var S=(2f*I-N)/N;
                    var T=(2f*J-N)/N;
                    Vertices.Add(new(Normal+U*S+V*T,Normal,new Vector2((float)I/N,(float)J/N)));
                }
            }
            for(var J=0;J<N;J++){
                for(var I=0;I<N;I++){
                    uint A=First+(uint)(J*(N+1)+I),B=A+1,C=A+(uint)(N+1),D=C+1;
                    Indices.AddRange([A,B,C,B,D,C]);
                }
            }
        }
        return ([..Vertices],[..Indices]);
    }
    private static void AssertNoFlip(SimplifierVertex[]Vertices,uint[]Indices,Vector3 Up){
        for(var Corner=0;Corner<Indices.Length;Corner+=3){
            var A=Vertices[(int)Indices[Corner]].Position;
            var B=Vertices[(int)Indices[Corner+1]].Position;
            var C=Vertices[(int)Indices[Corner+2]].Position;
            Assert.True(Vector3.Dot(Vector3.Cross(B-A,C-A),Up)>0f,"a triangle turned over");
        }
    }
    private static void AssertOutward(SimplifierVertex[]Vertices,uint[]Indices){
        for(var Corner=0;Corner<Indices.Length;Corner+=3){
            var A=Vertices[(int)Indices[Corner]].Position;
            var B=Vertices[(int)Indices[Corner+1]].Position;
            var C=Vertices[(int)Indices[Corner+2]].Position;
            var Centre=(A+B+C)/3f;
            Assert.True(Vector3.Dot(Vector3.Cross(B-A,C-A),Centre)>0f,"a sphere triangle faces inward");
        }
    }
}
