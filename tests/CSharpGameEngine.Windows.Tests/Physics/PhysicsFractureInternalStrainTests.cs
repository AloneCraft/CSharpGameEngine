using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// M0124：破壊ソルバーの内部ひずみ（UEのChaos内部ひずみ）とクラスターレベル。結合には過去の衝撃によるひずみを保持し、毎ティック
/// <see cref="FractureSettings.StrainRetention"/>分だけ減衰させる。そのため、小さなヒットを繰り返せば単発では破断しない結合も破断し、
/// 減衰後はヒットが消える。一定荷重ではひずみが蓄積しない。強度とちょうど等しい場合も破断し、再生した記録では内部ひずみが一致する。
/// 外側クラスタ間の結合が先に破断する。
/// </summary>
public sealed class PhysicsFractureInternalStrainTests {
    private static readonly LogicalScalar Retention=FractureSettings.RetentionFor(LogicalScalar.One/LogicalScalar.FromInt64(2),60);
    private static readonly FractureSettings Lasting=new(LogicalScalar.One,LogicalScalar.Zero,false,Retention);
    private static PrefractureAsset Crate(int Count=12,int Seed=3,int Levels=1)=>DestructionTests.Cook($"Strain{Count}-{Seed}-{Levels}",DestructionTests.Half(0.5f,0.5f,0.5f),Count,Seed,Levels:Levels);
    private static LogicalScalar Below(LogicalScalar Value)=>LogicalScalar.FromRaw(Value.Raw-1);
    private static LogicalScalar Fraction(LogicalScalar Value,int Numerator,int Denominator)=>Value*LogicalScalar.FromInt64(Numerator)/LogicalScalar.FromInt64(Denominator);
    [Fact]
    public void TheRetentionIsOneLessTheDecayTimesTheTickAndNeverBelowZero(){
        Assert.Equal(LogicalScalar.One-LogicalScalar.One/LogicalScalar.FromInt64(2)/LogicalScalar.FromInt64(60),Retention);
        Assert.Equal(LogicalScalar.One,FractureSettings.RetentionFor(LogicalScalar.Zero,60));
        Assert.Equal(LogicalScalar.Zero,FractureSettings.RetentionFor(LogicalScalar.FromInt64(60),60));
        Assert.Equal(LogicalScalar.Zero,FractureSettings.RetentionFor(LogicalScalar.FromInt64(500),60));
        Assert.Throws<ArgumentOutOfRangeException>(()=>FractureSettings.RetentionFor(-LogicalScalar.One,60));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new FractureState(Crate(),new FractureSettings(LogicalScalar.One,LogicalScalar.Zero,false,LogicalScalar.FromInt64(2))));
    }
    [Fact]
    public void OneSmallHitDoesNotBreakABondButThreeCloseTogetherDo(){
        var One=new FractureState(Crate(),Lasting);
        var Strength=One.StrengthOf(0);
        var Hit=Fraction(Strength,2,5);
        List<FractureBondBreak>Breaks=[];
        One.AddStrain(0,Hit);
        One.Resolve(Breaks);
        for(var Tick=0;Tick<600;Tick++){ One.Resolve(Breaks); }
        Assert.Empty(Breaks);
        Assert.False(One.IsBondBroken(0));
        // 0.1秒間隔で3回衝撃を与える。0.4 + 0.4 × 0.95 + …が3回目に強度へ達する。
        var Three=new FractureState(Crate(),Lasting);
        var BrokeAt=-1;
        for(var Tick=0;Tick<=12&&BrokeAt<0;Tick++){
            if(Tick%6==0){ Three.AddStrain(0,Hit); }
            Three.Resolve(Breaks);
            if(Three.IsBondBroken(0)){ BrokeAt=Tick; }
        }
        Assert.Equal(12,BrokeAt);
        Assert.Equal(0,Assert.Single(Breaks).Bond);
        Assert.True(Breaks[0].Strain>=Strength);
        Assert.Equal(LogicalScalar.Zero,Three.InternalStrainOf(0));
    }
    [Fact]
    public void TheInternalStrainFadesByTheRetentionEachTickAndEnds(){
        var State=new FractureState(Crate(),Lasting);
        var Strength=State.StrengthOf(0);
        var Hit=Fraction(Strength,2,5);
        State.AddStrain(0,Hit);
        State.Resolve([]);
        var Expected=Hit*Retention;
        Assert.Equal(Expected,State.InternalStrainOf(0));
        for(var Tick=0;Tick<120;Tick++){
            State.Resolve([]);
            Expected*=Retention;
            Assert.Equal(Expected,State.InternalStrainOf(0));
        }
        // 2秒後には約37 %が残る。強度の100万分の1（2⁻²⁰、この例では約26秒）未満になると消える。
        Assert.InRange(Expected.TryToSingle(out var Left)?Left/(Hit.TryToSingle(out var First)?First:1f):0f,0.36f,0.37f);
        for(var Tick=0;Tick<3000;Tick++){ State.Resolve([]); }
        Assert.Equal(LogicalScalar.Zero,State.InternalStrainOf(0));
        // 2秒間隔のヒットでは強度に達しない（最大でも約0.63）。ひずみ減衰の方が勝る。
        List<FractureBondBreak>Breaks=[];
        for(var Tick=0;Tick<120*20;Tick++){
            if(Tick%120==0){ State.AddStrain(0,Hit); }
            State.Resolve(Breaks);
        }
        Assert.Empty(Breaks);
    }
    [Fact]
    public void ASteadyLoadStrainsEachTickOnlyAndNeverWearsTheBondDown(){
        var State=new FractureState(Crate(),Lasting);
        var Load=Fraction(State.StrengthOf(0),9,10);
        List<FractureBondBreak>Breaks=[];
        for(var Tick=0;Tick<3000;Tick++){
            State.AddStrain(0,Load,Lasting:false);
            State.Resolve(Breaks);
        }
        Assert.Empty(Breaks);
        Assert.Equal(LogicalScalar.Zero,State.InternalStrainOf(0));
        // 同じ荷重を衝撃として与えると、2ティック目に破断する（0.9 + 0.9 × 0.99）。
        State.AddStrain(0,Load);
        State.Resolve(Breaks);
        Assert.Empty(Breaks);
        State.AddStrain(0,Load);
        State.Resolve(Breaks);
        Assert.Equal(0,Assert.Single(Breaks).Bond);
    }
    [Fact]
    public void ABondBreaksWhenItsInternalStrainAndTheTicksStrainReachExactlyItsStrength(){
        static FractureState Hit(out LogicalScalar Strength,out LogicalScalar Carried){
            var State=new FractureState(Crate(),Lasting);
            Strength=State.StrengthOf(0);
            State.AddStrain(0,Fraction(Strength,1,3));
            State.Resolve([]);
            Carried=State.InternalStrainOf(0);
            return State;
        }
        var Exact=Hit(out var Strength,out var Carried);
        Assert.True(Carried>LogicalScalar.Zero);
        List<FractureBondBreak>Breaks=[];
        Exact.AddStrain(0,Strength-Carried,Lasting:false);
        Exact.Resolve(Breaks);
        Assert.Equal([new FractureBondBreak(0,Strength)],Breaks);
        var Short=Hit(out _,out _);
        Breaks.Clear();
        Short.AddStrain(0,Below(Strength-Carried),Lasting:false);
        Short.Resolve(Breaks);
        Assert.Empty(Breaks);
    }
    [Fact]
    public void APlayedBackRecordingLeavesTheSameInternalStrainsAndPiecesAndTheLiveRunGoesOnAlike(){
        var Asset=Crate(24,7,2);
        var Settings=new FractureSettings(LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2),true,Retention);
        var Live=new FractureState(Asset,Settings);
        var Played=new FractureState(Asset,Settings);
        var Seed=12345UL;
        int Next(int Range){
            Seed=Seed*6364136223846793005UL+1442695040888963407UL;
            return (int)((Seed>>33)%(ulong)Range);
        }
        var Unit=Asset.Bonds.Min(static Bond=>Bond.Strength);
        List<FractureBondBreak>Breaks=[];
        List<FractureBondStrain>Raised=[];
        for(var Tick=0;Tick<400;Tick++){
            if(Next(5)==0){ Live.AddImpulse(Next(Asset.Fragments.Count),Fraction(Unit,1+Next(6),10)); }
            if(Next(3)==0){ Live.AddImpulse(Next(Asset.Fragments.Count),Fraction(Unit,1,4),Lasting:false); }
            Breaks.Clear();
            Raised.Clear();
            var LiveStep=Live.Resolve(Breaks,Raised);
            var PlayedStep=Played.Replay([.. Breaks.Select(static Break=>Break.Bond)],Raised);
            Assert.Equal(LiveStep.Began.Select(static Piece=>(Piece.Id,string.Join(",",Piece.Fragments))),PlayedStep.Began.Select(static Piece=>(Piece.Id,string.Join(",",Piece.Fragments))));
            for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
                Assert.Equal(Live.IsBondBroken(Bond),Played.IsBondBroken(Bond));
                Assert.Equal(Live.InternalStrainOf(Bond),Played.InternalStrainOf(Bond));
            }
        }
        Assert.True(Live.BrokenBondCount>0,"nothing broke in the recording");
        Assert.True(Live.BrokenBondCount<Asset.Bonds.Count,"everything broke in the recording");
        // 記録を再生した区間の後は、どちらも同じヒットでライブ実行を続け、同様に破断する。
        for(var Tick=0;Tick<200;Tick++){
            var Fragment=Next(Asset.Fragments.Count);
            var Impulse=Fraction(Unit,1+Next(6),10);
            List<FractureBondBreak>LiveBreaks=[];
            List<FractureBondBreak>PlayedBreaks=[];
            Live.AddImpulse(Fragment,Impulse);
            Played.AddImpulse(Fragment,Impulse);
            Live.Resolve(LiveBreaks);
            Played.Resolve(PlayedBreaks);
            Assert.Equal(LiveBreaks,PlayedBreaks);
        }
    }
    [Fact]
    public void TheBondsBetweenTheOuterClustersBreakFirstThenEachLevelInTurn(){
        // 破断していない各結合には、Cook時の強度×t / 8をティックごとに加える。そのため、強度が2^k倍のk階層内結合は、レベルごとに8 × 2^kティックで破断する。
        var Asset=Crate(48,5,3);
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.FromInt64(2),LogicalScalar.Zero,false));
        List<FractureBondBreak>Breaks=[];
        var LevelAt=new Dictionary<int,int>();
        for(var Tick=1;Tick<=64;Tick++){
            for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
                if(!State.IsBondBroken(Bond)){ State.AddStrain(Bond,Fraction(Asset.Bonds[Bond].Strength,Tick,8),Lasting:false); }
            }
            Breaks.Clear();
            State.Resolve(Breaks);
            foreach(var Break in Breaks){
                var Level=Asset.SharedClusterLevels(Asset.Bonds[Break.Bond].FirstFragment,Asset.Bonds[Break.Bond].SecondFragment);
                Assert.Equal(8<<Level,Tick);
                LevelAt[Level]=Tick;
            }
            if(Tick==8){
                // 最上位クラスタは、まとまりを保ったまま分離した。
                Assert.Equal(Asset.ClusterCount,State.PieceCount);
                Assert.All(State.Pieces,Piece=>Assert.Single(Piece.Fragments.Select(Fragment=>Asset.ClusterOf(Fragment,0)).Distinct()));
            }
            if(Tick==16){
                var SubClusters=Enumerable.Range(0,Asset.Fragments.Count).Select(Fragment=>Asset.ClusterOf(Fragment,1)).Distinct().Count();
                Assert.Equal(SubClusters,State.PieceCount);
            }
        }
        Assert.Equal([0,1,2,3],LevelAt.Keys.Order());
        Assert.Equal(Asset.Fragments.Count,State.PieceCount);
        Assert.Equal(Asset.Bonds.Count,State.BrokenBondCount);
    }
    [Fact]
    public void RepeatedHitsOnAnAnchoredSlabFreeOnlyItsUnanchoredParts(){
        var Asset=DestructionTests.Cook("StrainSlab",DestructionTests.Half(1f,1f,0.15f),16,4,Levels:2);
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.FromInt64(2),LogicalScalar.One/LogicalScalar.FromInt64(2),true,Retention));
        var Top=Enumerable.Range(0,Asset.Fragments.Count).MaxBy(Fragment=>Asset.Fragments[Fragment].LocalCenter.U);
        var Hit=Fraction(Asset.Bonds.Max(static Bond=>Bond.Strength),3,2);
        List<FractureBondBreak>Breaks=[];
        for(var Tick=0;Tick<120;Tick++){
            if(Tick%10==0){ State.AddImpulse(Top,Hit); }
            State.Resolve(Breaks);
        }
        Assert.NotEmpty(Breaks);
        Assert.Contains(State.Pieces,static Piece=>Piece.Anchored);
        Assert.Contains(State.Pieces,static Piece=>!Piece.Anchored);
        foreach(var Piece in State.Pieces){ Assert.Equal(Piece.Fragments.Any(State.IsAnchored),Piece.Anchored); }
    }
}
