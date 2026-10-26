using System.Text;
namespace CSharpGameEngine.Destruction.Tests;
/// <summary>
/// ADR-0078/M0113：エンジンのシード付き Voronoi クッカーと、Cook モジュールがコンパイルする <c>.cgefracture</c> バージョン 2 形式。すべてのフラグメントは外向きの閉じた体積で、体積の合計はソースを満たす。三角形がソース表面上にある場合に限り外面となり、各結合の面積は両フラグメントが共有する面の面積となる。クラスターは連結され、同じ入力からは同じバイト列が生成される。切り分けられない場合は FRACTUREnnn の理由とともに拒否する。
/// </summary>
public sealed class PrefractureCookerTests {
    private static LogicalScalar S(float Value)=>LogicalScalar.FromSingle(Value);
    private static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static PhysicsShapeAsset Box(LogicalVector3 Half)=>DestructibleScene.Box("source",LogicalVector3.Zero,Half);
    private static PrefractureAsset Cut(LogicalVector3 Half,int Count,int Seed,int Clusters=0){
        Assert.True(PrefractureCooker.TryGenerate(Box(Half),new PrefractureCookSettings(Count,Seed,LogicalScalar.FromInt64(500),LogicalScalar.FromInt64(2000),Clusters),out var Asset,out var Error),Error);
        return Asset!;
    }
    private static LogicalVector3 Half(float R,float U,float F)=>new(S(R),S(U),S(F));
    public static TheoryData<int,int,float,float,float>Cuts=>new(){
        { 2,0,0.5f,0.5f,0.5f },{ 6,1,0.5f,0.5f,0.5f },{ 12,3,0.5f,0.5f,0.5f },{ 24,11,0.15f,1.5f,2f },{ 32,-5,2f,0.25f,1f },{ 64,77,1f,1f,1f },
    };
    [Theory]
    [MemberData(nameof(Cuts))]
    public void EveryFragmentIsAClosedOutwardVolumeAndTheyFillTheSource(int Count,int Seed,float R,float U,float FHalf){
        var Asset=Cut(Half(R,U,FHalf),Count,Seed);
        Assert.Equal(Count,Asset.Fragments.Count);
        var Total=0f;
        for(var Index=0;Index<Count;Index++){
            var Mesh=Asset.Meshes[Index];
            Assert.True(Mesh.IsClosedManifold(),$"fragment {Index} is not closed");
            var Volume=F(Mesh.SignedVolume());
            Assert.True(Volume>0f,$"fragment {Index} winds inward ({Volume})");
            Total+=Volume;
            Assert.InRange(F(Asset.Fragments[Index].Mass),Volume*500f*0.999f,Volume*500f*1.001f);
        }
        var Expected=8f*R*U*FHalf;
        Assert.InRange(Total,Expected*0.999f,Expected*1.001f);
    }
    [Theory]
    [MemberData(nameof(Cuts))]
    public void ATriangleIsOuterExactlyWhenItLiesOnTheSourcesSurface(int Count,int Seed,float R,float U,float FHalf){
        var Asset=Cut(Half(R,U,FHalf),Count,Seed);
        var Limit=new[]{ R,U,FHalf };
        for(var Index=0;Index<Count;Index++){
            var Mesh=Asset.Meshes[Index];
            var Center=Asset.Fragments[Index].LocalCenter;
            for(var Triangle=0;Triangle<Mesh.Slots.Count;Triangle++){
                var Points=Enumerable.Range(0,3).Select(Corner=>Mesh.Vertices[Mesh.Indices[Triangle*3+Corner]]+Center).ToArray();
                // 切断面の細長い部分は、頂点がソース面から 0.1 mm 以内に収まることがあるため、三角形もその軸方向を向いていなければならない。
                if(!LogicalVector3.Cross(Points[1]-Points[0],Points[2]-Points[0]).TryNormalize(out var Normal)){ continue; }
                var OnFace=Enumerable.Range(0,3).Any(Axis=>Points.All(Point=>MathF.Abs(MathF.Abs(F(Axis==0?Point.R:Axis==1?Point.U:Point.F))-Limit[Axis])<0.0001f)
                    &&MathF.Abs(F(Axis==0?Normal.R:Axis==1?Normal.U:Normal.F))>0.999f);
                Assert.Equal(OnFace?FractureMesh.OuterSlot:FractureMesh.InteriorSlot,Mesh.Slots[Triangle]);
            }
        }
    }
    [Theory]
    [MemberData(nameof(Cuts))]
    public void EachFragmentsCutFacesAreItsBondsAndEveryBondIsSharedByBoth(int Count,int Seed,float R,float U,float FHalf){
        var Asset=Cut(Half(R,U,FHalf),Count,Seed);
        var Bonded=new float[Count];
        foreach(var Bond in Asset.Bonds){
            Assert.True(Bond.FirstFragment<Bond.SecondFragment);
            Assert.True(Bond.Area>LogicalScalar.Zero);
            Assert.Equal(Bond.Area*Asset.BondStrengthPerArea,Bond.Strength);
            Bonded[Bond.FirstFragment]+=F(Bond.Area);
            Bonded[Bond.SecondFragment]+=F(Bond.Area);
        }
        Assert.Equal(Asset.Bonds.Count,Asset.Bonds.Select(static Bond=>(Bond.FirstFragment,Bond.SecondFragment)).Distinct().Count());
        for(var Index=0;Index<Count;Index++){
            var Mesh=Asset.Meshes[Index];
            var Interior=0f;
            for(var Triangle=0;Triangle<Mesh.Slots.Count;Triangle++){
                if(Mesh.Slots[Triangle]!=FractureMesh.InteriorSlot){ continue; }
                var A=Mesh.Vertices[Mesh.Indices[Triangle*3]];
                var B=Mesh.Vertices[Mesh.Indices[Triangle*3+1]];
                var C=Mesh.Vertices[Mesh.Indices[Triangle*3+2]];
                Assert.True(LogicalScalar.TrySquareRoot(LogicalVector3.Cross(B-A,C-A).LengthSquared,out var Twice));
                Interior+=F(Twice)/2f;
            }
            // 1 平方ミリメートル未満の面は、結合ではなく頂点同士の接触である。
            Assert.InRange(Bonded[Index],Interior-0.0001f*Count,Interior+0.0001f);
        }
    }
    /// <summary>
    /// クラスターは、近傍が先に割り当てられない限り ⌈fragments / clusters⌉ 個まで成長する。このように小さいクラスターは、要求数を超える間は近傍へ結合する。そのため要求数以上の連結クラスターができる。クラスターが 1 つならオブジェクト全体であり、クラスター数がフラグメント数と同じなら階層はない。
    /// </summary>
    [Theory]
    [InlineData(16,0,4)]
    [InlineData(16,1,1)]
    [InlineData(16,5,5)]
    [InlineData(9,16,9)]
    [InlineData(12,0,3)]
    public void TheClustersAreConnectedAndNumberedByTheirFirstFragment(int Count,int Requested,int Wanted){
        var Asset=Cut(Half(0.5f,0.5f,0.5f),Count,7,Requested);
        Assert.InRange(Asset.ClusterCount,Wanted,Count);
        if(Requested==1){ Assert.Equal(1,Asset.ClusterCount); }
        if(Requested>=Count){ Assert.Equal(Count,Asset.ClusterCount); }
        Assert.True(Asset.IsCanonicalArtifact());
        var Next=0;
        foreach(var Cluster in Asset.Clusters){
            Assert.InRange(Cluster,0,Next);
            if(Cluster==Next){ Next++; }
        }
    }
    [Fact]
    public void TheSameCutGivesTheSameBytesAndTheBytesRoundTrip(){
        var First=PrefractureArtifact.ToBytes(Cut(Half(0.5f,1f,0.5f),20,42));
        var Second=PrefractureArtifact.ToBytes(Cut(Half(0.5f,1f,0.5f),20,42));
        Assert.Equal(First,Second);
        Assert.NotEqual(First,PrefractureArtifact.ToBytes(Cut(Half(0.5f,1f,0.5f),20,43)));
        Assert.True(PrefractureArtifact.TryRead(First,out var Read,out var Error),Error);
        Assert.Equal(First,PrefractureArtifact.ToBytes(Read!));
        Assert.Equal("CGEFRAC2",Encoding.ASCII.GetString(First,0,8));
    }
    [Fact]
    public void EveryTruncationAndATrailingByteAreRefusedWithoutThrowing(){
        var Bytes=PrefractureArtifact.ToBytes(Cut(Half(0.5f,0.5f,0.5f),3,1));
        for(var Length=0;Length<Bytes.Length;Length+=Math.Max(1,Bytes.Length/400)){
            Assert.False(PrefractureArtifact.TryRead(Bytes[..Length],out _,out _),$"{Length} bytes were read");
        }
        Assert.False(PrefractureArtifact.TryRead([..Bytes,0x7f],out _,out _));
    }
    [Fact]
    public void AVersionOneArtifactIsStillRead(){
        var Asset=Cut(Half(0.5f,0.5f,0.5f),4,2);
        var Version1=WriteVersion1(Asset);
        Assert.True(PrefractureArtifact.TryRead(Version1,out var Read,out var Error),Error);
        Assert.Equal(Asset.Fragments,Read!.Fragments);
        Assert.Empty(Read.Sites);
        Assert.Empty(Read.Clusters);
        Assert.Equal(Asset.Fragments.Count,Read.ClusterCount);
        Assert.All(Read.Meshes,static Mesh=>Assert.All(Mesh.Slots,static Slot=>Assert.Equal(FractureMesh.OuterSlot,Slot)));
        Assert.Equal(Asset.Bonds.Select(static Bond=>Bond.Strength),Read.Bonds.Select(static Bond=>Bond.Strength));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(257)]
    public void ACountOutsideTwoTo256IsRefused(int Count){
        Assert.False(PrefractureCooker.TryGenerate(Box(Half(0.5f,0.5f,0.5f)),new PrefractureCookSettings(Count,0,LogicalScalar.One,LogicalScalar.One),out _,out var Error));
        Assert.StartsWith("FRACTURE002",Error);
    }
    [Fact]
    public void AnOpenADegenerateAndAConcaveMeshAreRefused(){
        PhysicsShapeAsset Open=PhysicsShapeAsset.CreateStaticTriangleMesh("open",
            [new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,1,0),new LogicalVector3(0,0,1)],[0,1,2,0,3,1,0,2,3]);
        var Settings=new PrefractureCookSettings(3,1,LogicalScalar.One,LogicalScalar.One);
        Assert.False(PrefractureCooker.TryGenerate(Open,Settings,out _,out var OpenError));
        Assert.StartsWith("FRACTURE001",OpenError);
        Assert.False(PrefractureCooker.TryGenerate(PhysicsShapeAsset.CreateConvex("convex",[new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,1,0),new LogicalVector3(0,0,1)]),Settings,out _,out var KindError));
        Assert.StartsWith("FRACTURE001",KindError);
        // 2 つのボックスを L 字に配置：閉じているが凸ではない。
        LogicalVector3 V(float X,float Y,float Z)=>new(S(X),S(Y),S(Z));
        LogicalVector3[]Points=[V(0,0,0),V(2,0,0),V(2,1,0),V(1,1,0),V(1,2,0),V(0,2,0),V(0,0,1),V(2,0,1),V(2,1,1),V(1,1,1),V(1,2,1),V(0,2,1)];
        List<int>Indices=[];
        int[]Front=[0,5,4,0,4,3,0,3,2,0,2,1];
        Indices.AddRange(Front);
        Indices.AddRange(Front.Select(static Index=>Index+6).Chunk(3).SelectMany(static Triangle=>new[]{ Triangle[0],Triangle[2],Triangle[1] }));
        for(var Edge=0;Edge<6;Edge++){
            var A=Edge;
            var B=(Edge+1)%6;
            Indices.AddRange([A,B,B+6,A,B+6,A+6]);
        }
        var Concave=PhysicsShapeAsset.CreateStaticTriangleMesh("concave",Points,Indices);
        Assert.False(PrefractureCooker.TryGenerate(Concave,Settings,out _,out var ConcaveError));
        Assert.StartsWith("FRACTURE003",ConcaveError);
    }
    /// <summary>M0055 のレイアウト：設定、サイト、クラスター、スロット、面積がない。</summary>
    private static byte[]WriteVersion1(PrefractureAsset Asset){
        using MemoryStream Stream=new();
        using(BinaryWriter Writer=new(Stream,Encoding.UTF8,true)){
            Writer.Write("CGEFRAC1"u8);
            Writer.Write(1);
            Writer.Write(Asset.SourceHash);
            Writer.Write(Asset.Seed);
            Writer.Write(Asset.Fragments.Count);
            for(var Index=0;Index<Asset.Fragments.Count;Index++){
                var Fragment=Asset.Fragments[Index];
                Writer.Write(Fragment.Index);
                Writer.Write(Fragment.ShapeId);
                Vector(Writer,Fragment.LocalCenter);
                Scalar(Writer,Fragment.Mass);
                Writer.Write(Asset.Meshes[Index].Vertices.Count);
                foreach(var Vertex in Asset.Meshes[Index].Vertices){ Vector(Writer,Vertex); }
                Writer.Write(Asset.Meshes[Index].Indices.Count);
                foreach(var Value in Asset.Meshes[Index].Indices){ Writer.Write(Value); }
            }
            Writer.Write(Asset.Bonds.Count);
            foreach(var Bond in Asset.Bonds){
                Writer.Write(Bond.FirstFragment);
                Writer.Write(Bond.SecondFragment);
                Scalar(Writer,Bond.Strength);
            }
        }
        return Stream.ToArray();
        static void Vector(BinaryWriter Writer,LogicalVector3 Value){ Scalar(Writer,Value.R);Scalar(Writer,Value.U);Scalar(Writer,Value.F); }
        static void Scalar(BinaryWriter Writer,LogicalScalar Value){ Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];Value.WriteLittleEndian(Bytes);Writer.Write(Bytes); }
    }
}
