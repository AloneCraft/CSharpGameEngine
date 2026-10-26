using System.Text;
using CSharpGameEngine.Cooking;
using static CSharpGameEngine.Destruction.Tests.TestProject;
namespace CSharpGameEngine.Destruction.Tests;
/// <summary>
/// M0124：クラスターの Cook レベル（UE の Geometry Collection レベル）と、そのレベルを保存する <c>.cgefracture</c> バージョン 3。各レベルは一つ上のレベルにある各クラスターを連結クラスターへ分割し、最小フラグメント番号で番号を付ける。1 レベルは M0113 の分割とバイト単位で同一（バージョン 2）。レベル数が 1～4 の範囲外なら拒否する。バージョン 3 はバイト単位で往復し、破損したデータは例外を投げずに拒否する。Cook モジュールは <c>ClusterLevels</c> を読み取る。
/// </summary>
public sealed class ClusterLevelCookTests {
    private static LogicalScalar S(float Value)=>LogicalScalar.FromSingle(Value);
    private static LogicalVector3 Half(float R,float U,float F)=>new(S(R),S(U),S(F));
    private static PhysicsShapeAsset Box(LogicalVector3 Half)=>DestructibleScene.Box("source",LogicalVector3.Zero,Half);
    private static PrefractureCookSettings Settings(int Count,int Seed,int Levels,int Clusters=0)=>new(Count,Seed,LogicalScalar.FromInt64(500),LogicalScalar.FromInt64(2000),Clusters,Levels);
    private static PrefractureAsset Cut(int Count,int Seed,int Levels,int Clusters=0){
        Assert.True(PrefractureCooker.TryGenerate(Box(Half(1f,0.5f,1f)),Settings(Count,Seed,Levels,Clusters),out var Asset,out var Error),Error);
        return Asset!;
    }
    public static TheoryData<int,int,int>Levels=>new(){ { 24,5,2 },{ 48,7,2 },{ 64,2,3 },{ 30,9,4 } };
    [Theory]
    [MemberData(nameof(Levels))]
    public void EachLevelSplitsTheClustersAboveIntoConnectedCanonicallyNumberedClusters(int Count,int Seed,int LevelCount){
        var Asset=Cut(Count,Seed,LevelCount);
        Assert.Equal(LevelCount,Asset.ClusterLevelCount);
        Assert.True(Asset.IsCanonicalArtifact());
        var Previous=0;
        for(var Level=0;Level<LevelCount;Level++){
            var Numbers=Enumerable.Range(0,Count).Select(Fragment=>Asset.ClusterOf(Fragment,Level)).ToArray();
            // 正規形式：各クラスターの最小フラグメント順に番号を付ける。
            var Next=0;
            foreach(var Number in Numbers){
                Assert.InRange(Number,0,Next);
                if(Number==Next){ Next++; }
            }
            // 深いレベルではクラスター数が同数以上になり、それぞれが一つ上のレベルの単一クラスター内に収まる。
            Assert.True(Next>=Previous,$"level {Level} has {Next} clusters, fewer than {Previous} above");
            Previous=Next;
            if(Level==0){ continue; }
            foreach(var Group in Enumerable.Range(0,Count).GroupBy(Fragment=>Numbers[Fragment])){
                Assert.Single(Group.Select(Fragment=>Asset.ClusterOf(Fragment,Level-1)).Distinct());
                // クラスター内の結合を介して連結されている。
                HashSet<int>Members=[.. Group];
                HashSet<int>Reached=[Group.First()];
                for(var Grew=true;Grew;){
                    Grew=false;
                    foreach(var Bond in Asset.Bonds){
                        if(Members.Contains(Bond.FirstFragment)&&Members.Contains(Bond.SecondFragment)&&Reached.Contains(Bond.FirstFragment)!=Reached.Contains(Bond.SecondFragment)){
                            Reached.Add(Bond.FirstFragment);
                            Reached.Add(Bond.SecondFragment);
                            Grew=true;
                        }
                    }
                }
                Assert.Equal(Members.Count,Reached.Count);
            }
        }
        // 最下位レベルでもフラグメントをまとめる（クラスターサイズの √）。したがって最深部のクラスターは最上位より小さいが、単一フラグメントだけにはならない。
        Assert.True(Previous<Count||Count<=3,$"the deepest level has a cluster per fragment ({Previous})");
        // 結合が共有するレベル：最上位クラスター間では 0、最深クラスター内ではレベル数まで。
        foreach(var Bond in Asset.Bonds){
            var Shared=Asset.SharedClusterLevels(Bond.FirstFragment,Bond.SecondFragment);
            Assert.InRange(Shared,0,LevelCount);
            for(var Level=0;Level<LevelCount;Level++){ Assert.Equal(Level<Shared,Asset.ClusterOf(Bond.FirstFragment,Level)==Asset.ClusterOf(Bond.SecondFragment,Level)); }
        }
        Assert.Contains(Asset.Bonds,Bond=>Asset.SharedClusterLevels(Bond.FirstFragment,Bond.SecondFragment)==0);
        Assert.Contains(Asset.Bonds,Bond=>Asset.SharedClusterLevels(Bond.FirstFragment,Bond.SecondFragment)>=2);
    }
    [Fact]
    public void OneLevelIsM0113sCutByteForByteInVersionTwo(){
        // 既定値 (1) と明示的な 1 は、M0113 が書き出したバイト列と同じになる：バージョン 2、最上位クラスターのみ。
        Assert.True(PrefractureCooker.TryGenerate(Box(Half(1f,0.5f,1f)),new PrefractureCookSettings(24,5,LogicalScalar.FromInt64(500),LogicalScalar.FromInt64(2000)),out var Default,out var Error),Error);
        var One=Cut(24,5,1);
        var Bytes=PrefractureArtifact.ToBytes(One);
        Assert.Equal(PrefractureArtifact.ToBytes(Default!),Bytes);
        Assert.Equal("CGEFRAC2",Encoding.ASCII.GetString(Bytes,0,8));
        Assert.Equal(2,PrefractureArtifact.VersionOf(One));
        Assert.Equal(1,One.ClusterLevelCount);
        Assert.Empty(One.SubClusters);
        // より深い分割の最上位レベルは、同じ 1 レベル分割である。
        var Two=Cut(24,5,2);
        Assert.Equal(One.Clusters,Two.Clusters);
        Assert.Equal(One.Bonds,Two.Bonds);
    }
    [Fact]
    public void AMultiLevelCutIsVersionThreeAndRoundTripsByteForByte(){
        var Asset=Cut(48,7,3);
        var Bytes=PrefractureArtifact.ToBytes(Asset);
        Assert.Equal("CGEFRAC3",Encoding.ASCII.GetString(Bytes,0,8));
        Assert.Equal(3,BitConverter.ToInt32(Bytes,8));
        Assert.Equal(Bytes,PrefractureArtifact.ToBytes(Cut(48,7,3)));
        Assert.True(PrefractureArtifact.TryRead(Bytes,out var Read,out var Error),Error);
        Assert.Equal(3,Read!.ClusterLevelCount);
        for(var Level=0;Level<3;Level++){
            Assert.Equal(Enumerable.Range(0,48).Select(Fragment=>Asset.ClusterOf(Fragment,Level)),Enumerable.Range(0,48).Select(Fragment=>Read.ClusterOf(Fragment,Level)));
        }
        Assert.Equal(Bytes,PrefractureArtifact.ToBytes(Read));
    }
    [Fact]
    public void ATruncatedATrailingAndANonNestedVersionThreeFileAreRefusedWithoutThrowing(){
        var Asset=Cut(12,3,2);
        var Bytes=PrefractureArtifact.ToBytes(Asset);
        for(var Length=0;Length<Bytes.Length;Length+=Math.Max(1,Bytes.Length/300)){
            Assert.False(PrefractureArtifact.TryRead(Bytes[..Length],out _,out _),$"{Length} bytes were read");
        }
        Assert.False(PrefractureArtifact.TryRead([..Bytes,0x01],out _,out _));
        // 第 2 レベルで、異なる最上位クラスターに属する 2 つのフラグメントを同じクラスターにまとめるアセットは正規形式ではない。
        var Broken=new PrefractureAsset {
            SourceHash=Asset.SourceHash,Seed=Asset.Seed,Fragments=Asset.Fragments,Bonds=Asset.Bonds,Meshes=Asset.Meshes,CollisionShapes=Asset.CollisionShapes,
            Sites=Asset.Sites,Clusters=Asset.Clusters,SubClusters=[new int[Asset.Fragments.Count]],Density=Asset.Density,BondStrengthPerArea=Asset.BondStrengthPerArea,
        };
        Assert.True(Asset.ClusterCount>1);
        Assert.False(Broken.IsDeterministicallyOrdered());
        Assert.Throws<ArgumentException>(()=>PrefractureArtifact.ToBytes(Broken));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ALevelCountOutsideOneToFourIsRefused(int Levels){
        Assert.False(PrefractureCooker.TryGenerate(Box(Half(0.5f,0.5f,0.5f)),Settings(8,1,Levels),out _,out var Error));
        Assert.StartsWith("FRACTURE002",Error,StringComparison.Ordinal);
    }
    [Fact]
    public void TheCookModuleCutsTheLevelsAScenesDestructibleAsksForAndRefusesTooMany(){
        using var Project=new TestProject();
        var Output=Path.Combine(Project.Root,"out");
        Directory.CreateDirectory(Output);
        var Scene=Project.WriteScene("Level.json","Level",Node("Level",[],Node("Crate",[Transform(U:0.5f),BoxCollider(0.5f,0.5f,0.5f),Destructible("Crate",""","FragmentCount": 24, "Seed": 3, "ClusterLevels": 2""")])));
        var Result=new FractureCooker().Cook(new CookRequest(Scene,Output,Project.Root));
        Assert.True(Result.Succeeded,string.Join("\n",Result.Messages));
        Assert.True(PrefractureArtifact.TryRead(File.ReadAllBytes(Path.Combine(Output,"Assets","Destruction","Crate.cgefracture")),out var Asset,out var Error),Error);
        Assert.Equal(2,Asset!.ClusterLevelCount);
        Assert.Contains(Result.Messages,Message=>Message.Contains("2 cluster level(s)",StringComparison.Ordinal));
        var TooMany=Project.WriteScene("Deep.json","Deep",Node("Deep",[],Node("Crate",[Transform(U:0.5f),BoxCollider(0.5f,0.5f,0.5f),Destructible("Deep",""","ClusterLevels": 5""")])));
        var Refused=new FractureCooker().Cook(new CookRequest(TooMany,Output,Project.Root));
        Assert.False(Refused.Succeeded);
        Assert.Contains(Refused.Messages,static Message=>Message.Contains("error DST003",StringComparison.Ordinal));
    }
}
