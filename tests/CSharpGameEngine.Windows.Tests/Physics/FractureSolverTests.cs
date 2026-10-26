using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0113：破壊ソルバー（Runtime.Physicsの<see cref="FractureState"/>）。1ティックに蓄積したひずみが強度に達すると結合が破断する
///（同値なら破断）。力積はリングごとに広がり、クラスタ内部の結合ほど強い。分離したパーツは破片順の破片になり、固定された破片の子パーツは動かない。
/// また、記録された破断結合からも、ひずみで破断した場合と同じ破片を作る。
/// </summary>
public sealed class FractureSolverTests {
    private static readonly FractureSettings Plain=new(LogicalScalar.One,LogicalScalar.Zero,false);
    private static PrefractureAsset Crate(int Count=12,int Seed=3,int Clusters=0)=>DestructionTests.Cook($"Solver{Count}-{Seed}-{Clusters}",DestructionTests.Half(0.5f,0.5f,0.5f),Count,Seed,Clusters:Clusters);
    private static LogicalScalar Below(LogicalScalar Value)=>LogicalScalar.FromRaw(Value.Raw-1);
    [Fact]
    public void ABondBreaksAtExactlyItsStrengthAndNotOneStepBelow(){
        var State=new FractureState(Crate(),Plain);
        var Strength=State.StrengthOf(0);
        State.AddStrain(0,Below(Strength));
        List<FractureBondBreak>Breaks=[];
        Assert.False(State.Resolve(Breaks).Changed&&Breaks.Count>0);
        Assert.Empty(Breaks);
        State.AddStrain(0,Strength);
        State.Resolve(Breaks);
        Assert.Equal([new FractureBondBreak(0,Strength)],Breaks);
        Assert.True(State.IsBondBroken(0));
        Assert.Equal(1,State.BrokenBondCount);
    }
    [Fact]
    public void StrainDoesNotCarryFromOneTickToTheNext(){
        var State=new FractureState(Crate(),Plain);
        var Half=State.StrengthOf(0)/LogicalScalar.FromInt64(2);
        List<FractureBondBreak>Breaks=[];
        for(var Tick=0;Tick<10;Tick++){
            State.AddStrain(0,Half);
            State.Resolve(Breaks);
        }
        Assert.Empty(Breaks);
        Assert.Equal(LogicalScalar.Zero,State.StrainOf(0));
    }
    [Fact]
    public void AnImpulseLoadsTheHitFragmentsBondsAndThenEachRingByThePropagation(){
        var Asset=Crate();
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.One,LogicalScalar.One/LogicalScalar.FromInt64(2),false));
        var Impulse=LogicalScalar.FromInt64(64);
        State.AddImpulse(0,Impulse);
        // 結合のリング数は、その両端の破片から破片0までのホップ数の小さい方。
        var Hops=new int[Asset.Fragments.Count];
        Array.Fill(Hops,-1);
        Hops[0]=0;
        for(var Changed=true;Changed;){
            Changed=false;
            foreach(var Bond in Asset.Bonds){
                foreach(var (From,To) in new[]{ (Bond.FirstFragment,Bond.SecondFragment),(Bond.SecondFragment,Bond.FirstFragment) }){
                    if(Hops[From]>=0&&(Hops[To]<0||Hops[To]>Hops[From]+1)){ Hops[To]=Hops[From]+1;Changed=true; }
                }
            }
        }
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
            var Ring=Math.Min(Hops[Asset.Bonds[Bond].FirstFragment],Hops[Asset.Bonds[Bond].SecondFragment]);
            var Expected=Ring>4?LogicalScalar.Zero:Impulse/LogicalScalar.FromInt64(1L<<Ring);
            Assert.Equal(Expected,State.StrainOf(Bond));
        }
    }
    [Fact]
    public void ABondInsideAClusterIsTheScaleTimesStrongerThanOneBetweenClusters(){
        var Asset=Crate(16,5,4);
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.FromInt64(3),LogicalScalar.Zero,false));
        var Inner=0;
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
            var Value=Asset.Bonds[Bond];
            var Same=Asset.ClusterOf(Value.FirstFragment)==Asset.ClusterOf(Value.SecondFragment);
            Inner+=Same?1:0;
            Assert.Equal(Same?Value.Strength*LogicalScalar.FromInt64(3):Value.Strength,State.StrengthOf(Bond));
        }
        Assert.InRange(Inner,1,Asset.Bonds.Count-1);
    }
    [Fact]
    public void CuttingEveryBondOfAFragmentFreesItAsAPieceAndTheRestStaysOne(){
        var Asset=Crate();
        var State=new FractureState(Asset,Plain);
        var Cut=Enumerable.Range(0,Asset.Bonds.Count).Where(Bond=>Asset.Bonds[Bond].FirstFragment==0||Asset.Bonds[Bond].SecondFragment==0).ToArray();
        foreach(var Bond in Cut){ State.AddStrain(Bond,State.StrengthOf(Bond)); }
        var Step=State.Resolve([]);
        Assert.True(Step.Changed);
        Assert.True(Assert.Single(Step.Ended).IsWhole);
        Assert.Equal(2,Step.Began.Count);
        Assert.Equal([0],Step.Began[0].Fragments);
        Assert.Equal(Enumerable.Range(1,Asset.Fragments.Count-1),Step.Began[1].Fragments);
        Assert.All(Step.Began,Piece=>Assert.Equal(0,Piece.Parent));
        Assert.False(State.IsWhole);
        Assert.Equal(Asset.Fragments[0].Mass,Step.Began[0].Mass);
        Assert.Equal(Asset.Fragments[0].LocalCenter,Step.Began[0].Center);
        // 何も分離しない亀裂では、パーツに変化はない。
        var Rest=Step.Began[1];
        var Inner=Enumerable.Range(0,Asset.Bonds.Count).First(Bond=>!State.IsBondBroken(Bond));
        State.AddStrain(Inner,State.StrengthOf(Inner));
        var Crack=State.Resolve([]);
        Assert.True(State.IsBondBroken(Inner));
        Assert.True(!Crack.Changed||Crack.Ended.Single()==Rest);
    }
    [Fact]
    public void TheBondsOfARecordedTickMakeTheSamePiecesAsTheStrainThatBrokeThem(){
        var Asset=Crate(24,9);
        var Live=new FractureState(Asset,new FractureSettings(LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2),false));
        var Played=new FractureState(Asset,new FractureSettings(LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2),false));
        for(var Tick=0;Tick<5;Tick++){
            Live.AddImpulse(Tick*3%Asset.Fragments.Count,Live.StrengthOf(0)*LogicalScalar.FromInt64(4));
            List<FractureBondBreak>Breaks=[];
            var Expected=Live.Resolve(Breaks);
            var Actual=Played.Apply([.. Breaks.Select(static Break=>Break.Bond)]);
            Assert.Equal(Expected.Ended.Select(static Piece=>Piece.Id),Actual.Ended.Select(static Piece=>Piece.Id));
            Assert.Equal(Expected.Began.Select(static Piece=>(Piece.Id,Piece.Parent,string.Join(",",Piece.Fragments))),Actual.Began.Select(static Piece=>(Piece.Id,Piece.Parent,string.Join(",",Piece.Fragments))));
        }
        Assert.True(Live.BrokenBondCount>0);
        Assert.Equal(Live.BrokenBondCount,Played.BrokenBondCount);
    }
    [Fact]
    public void TheBottomFragmentsAreAnchoredAndHoldTheirPiecesStill(){
        var Asset=Crate(16,2);
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.One,LogicalScalar.Zero,true));
        for(var Fragment=0;Fragment<Asset.Fragments.Count;Fragment++){
            var Bottom=Asset.Meshes[Fragment].Vertices.Min(Vertex=>Vertex.U+Asset.Fragments[Fragment].LocalCenter.U);
            Assert.Equal(Bottom<=LogicalScalar.FromSingle(-0.49f),State.IsAnchored(Fragment));
        }
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){ State.AddStrain(Bond,State.StrengthOf(Bond)); }
        var Step=State.Resolve([]);
        Assert.Equal(Asset.Fragments.Count,Step.Began.Count);
        Assert.All(Step.Began,Piece=>Assert.Equal(State.IsAnchored(Piece.Fragments[0]),Piece.Anchored));
        Assert.Contains(Step.Began,static Piece=>Piece.Anchored);
        Assert.Contains(Step.Began,static Piece=>!Piece.Anchored);
    }
    [Fact]
    public void ASlabOfAnchorsBreaksIntoAnchoredPiecesOnly(){
        var Asset=DestructionTests.Cook("Slab",DestructionTests.Half(1f,0.004f,1f),6,1);
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.One,LogicalScalar.Zero,true));
        Assert.All(Enumerable.Range(0,Asset.Fragments.Count),Fragment=>Assert.True(State.IsAnchored(Fragment)));
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){ State.AddStrain(Bond,State.StrengthOf(Bond)); }
        var Step=State.Resolve([]);
        Assert.All(Step.Began,static Piece=>Assert.True(Piece.Anchored));
    }
    [Fact]
    public void APointGoesToTheFragmentOfTheNearestSite(){
        var Asset=Crate();
        var State=new FractureState(Asset,Plain);
        var Whole=State.Pieces.Single();
        for(var Fragment=0;Fragment<Asset.Fragments.Count;Fragment++){
            Assert.Equal(Fragment,State.FragmentAt(Asset.Sites[Fragment],Whole));
            Assert.Equal(Fragment,State.FragmentAt(Asset.Fragments[Fragment].LocalCenter,Whole));
        }
    }
    [Fact]
    public void APieceOfOneFragmentCollidesWithItsPolyhedronAndALargerOneWithACompound(){
        var Asset=Crate();
        var State=new FractureState(Asset,Plain);
        foreach(var Bond in Enumerable.Range(0,Asset.Bonds.Count).Where(Bond=>Asset.Bonds[Bond].FirstFragment==0||Asset.Bonds[Bond].SecondFragment==0)){ State.AddStrain(Bond,State.StrengthOf(Bond)); }
        var Step=State.Resolve([]);
        Assert.Same(Asset.CollisionShapes[0],State.ShapeOf(Step.Began[0]));
        var Compound=State.ShapeOf(Step.Began[1]);
        Assert.Equal(PhysicsShapeAssetKind.Compound,Compound.Kind);
        Assert.Equal(Asset.Fragments.Count-1,Compound.Children.Count);
        Assert.Same(Compound,State.ShapeOf(Step.Began[1]));
        Assert.All(Compound.Children,static Child=>Assert.True(Child.PlaneCount>=4));
    }
    [Fact]
    public void ARemovedPieceTakesNoMoreStrainAndBadInputsAreIgnored(){
        var Asset=Crate();
        var State=new FractureState(Asset,Plain);
        foreach(var Bond in Enumerable.Range(0,Asset.Bonds.Count).Where(Bond=>Asset.Bonds[Bond].FirstFragment==0||Asset.Bonds[Bond].SecondFragment==0)){ State.AddStrain(Bond,State.StrengthOf(Bond)); }
        var Step=State.Resolve([]);
        Assert.True(State.Remove(Step.Began[1].Id));
        Assert.Null(State.PieceOf(1));
        State.AddImpulse(1,LogicalScalar.FromInt64(1_000_000));
        State.AddImpulse(-1,LogicalScalar.FromInt64(10));
        State.AddImpulse(999,LogicalScalar.FromInt64(10));
        State.AddImpulse(0,-LogicalScalar.One);
        State.AddStrain(-5,LogicalScalar.One);
        Assert.All(Enumerable.Range(0,Asset.Bonds.Count),Bond=>Assert.Equal(LogicalScalar.Zero,State.StrainOf(Bond)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FractureState(Asset,new FractureSettings(LogicalScalar.Zero,LogicalScalar.Zero,false)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FractureState(Asset,new FractureSettings(LogicalScalar.One,LogicalScalar.FromInt64(2),false)));
    }
}
