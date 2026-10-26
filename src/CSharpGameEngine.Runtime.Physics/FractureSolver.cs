namespace CSharpGameEngine.Runtime.Physics;
/// <summary>
/// プロセスが読み込んだ調理済み事前破砕アセット（ADR-0078/M0113）。完全なパスで管理し、アセットは不変なので破壊プラグインとレンダラーで共有する。ファイルの長さまたは更新時刻が変わると再読み
/// 込みする。<see cref="Register"/> はメモリ上で作成したアセットをパスに登録する（テストとプレビュー用）。<see cref="Forget"/> が呼ばれるまではファイルより優先
/// される。
/// </summary>
public static class PrefractureAssetCache {
    private static readonly Dictionary<string,(PrefractureAsset Asset,long Length,DateTime Written,bool Registered)>Entries=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate=new();
    public static bool TryGet(string FullPath,out PrefractureAsset Asset,out string Error){
        ArgumentException.ThrowIfNullOrWhiteSpace(FullPath);
        Asset=null!;
        Error=string.Empty;
        lock(Gate){
            if(Entries.TryGetValue(FullPath,out var Entry)&&Entry.Registered){
                Asset=Entry.Asset;
                return true;
            }
            FileInfo File=new(FullPath);
            if(!File.Exists){
                Error=$"The prefracture asset '{FullPath}' was not found (the build cooks it from the scene's Destructible).";
                return false;
            }
            if(Entries.TryGetValue(FullPath,out Entry)&&Entry.Length==File.Length&&Entry.Written==File.LastWriteTimeUtc){
                Asset=Entry.Asset;
                return true;
            }
            byte[]Bytes;
            try { Bytes=System.IO.File.ReadAllBytes(FullPath); }
            catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException){
                Error=$"The prefracture asset '{FullPath}' could not be read: {Exception.Message}";
                return false;
            }
            if(!PrefractureArtifact.TryRead(Bytes,out var Loaded,out var ReadError)||Loaded is null){
                Error=$"The prefracture asset '{FullPath}' is not valid: {ReadError}";
                return false;
            }
            Entries[FullPath]=(Loaded,File.Length,File.LastWriteTimeUtc,false);
            Asset=Loaded;
            return true;
        }
    }
    public static void Register(string FullPath,PrefractureAsset Asset){
        ArgumentException.ThrowIfNullOrWhiteSpace(FullPath);
        ArgumentNullException.ThrowIfNull(Asset);
        lock(Gate){ Entries[FullPath]=(Asset,0,default,true); }
    }
    public static void Forget(string FullPath){
        lock(Gate){ Entries.Remove(FullPath); }
    }
}
/// <summary>
/// 実行時に破壊可能オブジェクトがどのように壊れるかを設定する（ADR-0078/M0113）。<see cref="ClusterStrengthScale"/> は、調理済み階層のうち両方の破片を含む
/// 各段のクラスターについて、ボンドの強度を一度ずつ乗算する（UE の階層別ダメージしきい値。M0124：最上位クラスター間のボンドが最も弱く、衝撃でまずクラスター全体が外れ、より強い衝撃で下位クラスター
/// 、さらに強い衝撃で個々の破片が分離する）。<see cref="DamagePropagation"/> は、力積のうち衝撃点から一つ先の破片のボンドに伝わる割合（UE のダメージ伝播係数）で、隣接輪
/// ごとに適用する。<see cref="StrainRetention"/>（M0124、0～1）は、ボンドの内部ひずみを次のティックにどれだけ保持するかを示す（1 − decay × Δt）。既定値の
/// 0では保持しない（毎ティックで独立する M0113 の動作）。1ではすべて保持する。
/// </summary>
public readonly record struct FractureSettings(LogicalScalar ClusterStrengthScale,LogicalScalar DamagePropagation,bool AnchorBottom,LogicalScalar StrainRetention=default){
    /// <summary>減衰率（1/s）とティックレート（ティック/s）から求める保持率。速度の物理減衰と同様、max(0, 1 − decay / rate)。</summary>
    public static LogicalScalar RetentionFor(LogicalScalar DecayPerSecond,int TicksPerSecond){
        if(DecayPerSecond<LogicalScalar.Zero||TicksPerSecond<=0){ throw new ArgumentOutOfRangeException(nameof(DecayPerSecond),"The strain decay must be zero or more and the tick rate positive."); }
        var Kept=LogicalScalar.One-DecayPerSecond/LogicalScalar.FromInt64(TicksPerSecond);
        return Kept<LogicalScalar.Zero?LogicalScalar.Zero:Kept;
    }
}
/// <summary>ステップ中に切れたボンドと、それを切ったひずみ（内部ひずみとこのティックのひずみの合計）。</summary>
public readonly record struct FractureBondBreak(int Bond,LogicalScalar Strain);
/// <summary>ステップ中に衝撃で内部ひずみが増加したボンドと、次のステップへ持ち越す内部ひずみ（M0124）。</summary>
public readonly record struct FractureBondStrain(int Bond,LogicalScalar Strain);
/// <summary>
/// 破壊可能オブジェクトの破片1つ。連結した破片の集合が一体のボディとして動く（UE の cluster union）。<see cref="Center"/> は元形状の座標枠における重心で、ボディの
/// 原点になる。破片0はオブジェクト全体を表し、最初に分割されるまでは設定された破壊可能オブジェクト自体のボディを使う。
/// </summary>
public sealed class FracturePiece(int Id,int[]Fragments,bool Anchored,LogicalVector3 Center,LogicalScalar Mass,int Parent) {
    public int Id { get; }=Id;
    /// <summary>昇順に並んだ破片。</summary>
    public IReadOnlyList<int>Fragments { get; }=Fragments;
    /// <summary>破片が固定されているか。固定されている場合、その破片を静的ボディとして動かさない。</summary>
    public bool Anchored { get; }=Anchored;
    public LogicalVector3 Center { get; }=Center;
    public LogicalScalar Mass { get; }=Mass;
    /// <summary>分離元の破片（そのボディの動きを引き継ぐ）。オブジェクト全体の場合は -1。</summary>
    public int Parent { get; }=Parent;
    public bool IsWhole=>this.Id==0;
}
/// <summary>ステップで変化した内容。終了した破片（分割または削除）と、新しく始まった破片を、ボディが作成される順に並べる。</summary>
public sealed class FractureStep {
    public static readonly FractureStep None=new([],[]);
    internal FractureStep(IReadOnlyList<FracturePiece>Ended,IReadOnlyList<FracturePiece>Began){
        this.Ended=Ended;
        this.Began=Began;
    }
    public IReadOnlyList<FracturePiece>Ended { get; }
    public IReadOnlyList<FracturePiece>Began { get; }
    public bool Changed=>this.Ended.Count>0||this.Began.Count>0;
}
/// <summary>
/// 破壊可能オブジェクト1つ分の破壊状態（ADR-0078/M0113）。UE の Chaos クラスタリングソルバーを ECS から独立させ、調理処理、実行時処理、テストで共有する。ティック中、接触力積
/// （<see cref="AddImpulse"/>：衝撃を受けた破片のボンドには全量を加え、次の隣接輪には
/// <see cref="FractureSettings.DamagePropagation"/> の割合、その先も同様）と、ひずみフィールド（<see cref="AddStrainSphere"/>
/// ）からボンドにひずみ（力積、N·s）を集める。M0124：ボンドはティック間で内部ひずみ（UE Chaos の蓄積ダメージ）も保持する。<see cref="Resolve"/> は内部ひずみとこの
/// ティックのひずみの合計が強度以上（同値を含む）になったボンドをすべて切る。次に、各未破壊ボンドがこのティックの持続するひずみ（衝撃：接触開始時のティック、ゲームからの力積、キャラクターの衝突、
/// フィールド）を受け取り、そのうち <see cref="FractureSettings.StrainRetention"/> の割合を保持する。物体に載り続けるボディの静的荷重は含めない。その重さ
/// 自体でボンドが徐々に切れることを防ぐためである。この仕組みにより、一度では切れないボンドも小さな衝撃の繰り返しで切れ、古い衝撃の影響は薄れていく。このティックのひずみを破棄し、まとまりを失った各破片を
/// 連結部分に分割する。固定破片（<see cref="FractureSettings.AnchorBottom"/>：元形状の最下部の面にある破片）は、その破片を静止させる。すべて整数または
/// Q64.64 で計算し、インデックス順に処理するため、同じ入力からは常に同じ破片と内部ひずみが得られる。
/// </summary>
public sealed class FractureState {
    private const int MaximumRings=4;
    private readonly LogicalScalar[]Strength;
    private readonly LogicalScalar[]Strain;
    /// <summary>ティックのひずみのうち持続する部分（衝撃）。ティック終了時に <see cref="Internal"/> へ加える。</summary>
    private readonly LogicalScalar[]Lasting;
    /// <summary>各ボンドがティック間で保持する内部ひずみ（M0124）。</summary>
    private readonly LogicalScalar[]Internal;
    private readonly bool[]Broken;
    private readonly bool[]Anchored;
    private readonly int[][]FragmentBonds;
    private readonly int[]PieceOfFragment;
    private readonly int[]Distance;
    private readonly SortedDictionary<int,FracturePiece>LivePieces=[];
    private readonly Dictionary<string,PhysicsShapeAsset>Shapes=new(StringComparer.Ordinal);
    private int NextPiece;
    public FractureState(PrefractureAsset Asset,FractureSettings Settings){
        ArgumentNullException.ThrowIfNull(Asset);
        if(Settings.ClusterStrengthScale<=LogicalScalar.Zero||Settings.DamagePropagation<LogicalScalar.Zero||Settings.DamagePropagation>LogicalScalar.One
            ||Settings.StrainRetention<LogicalScalar.Zero||Settings.StrainRetention>LogicalScalar.One){
            throw new ArgumentOutOfRangeException(nameof(Settings),"The cluster strength scale must be positive, and the damage propagation and the strain retention between 0 and 1.");
        }
        this.Asset=Asset;
        this.Settings=Settings;
        var Count=Asset.Fragments.Count;
        this.Strength=new LogicalScalar[Asset.Bonds.Count];
        this.Strain=new LogicalScalar[Asset.Bonds.Count];
        this.Lasting=new LogicalScalar[Asset.Bonds.Count];
        this.Internal=new LogicalScalar[Asset.Bonds.Count];
        this.Broken=new bool[Asset.Bonds.Count];
        List<int>[]Lists=new List<int>[Count];
        for(var Index=0;Index<Count;Index++){ Lists[Index]=[]; }
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
            var Value=Asset.Bonds[Bond];
            // 両方の破片を同じ階層のクラスターが含む場合、各階層で一度適用する（M0124。一階層なら同じクラスター内で一度、M0113 と同様）。
            var Strength=Value.Strength;
            for(var Level=Asset.SharedClusterLevels(Value.FirstFragment,Value.SecondFragment);Level>0;Level--){ Strength*=Settings.ClusterStrengthScale; }
            this.Strength[Bond]=Strength;
            Lists[Value.FirstFragment].Add(Bond);
            Lists[Value.SecondFragment].Add(Bond);
        }
        this.FragmentBonds=[.. Lists.Select(static List=>List.ToArray())];
        this.Anchored=new bool[Count];
        if(Settings.AnchorBottom){
            // 元形状の最下点から1 cm以内に届く破片は、元形状が載っていた面に接地する。
            var Lowest=LogicalScalar.MaxValue;
            var Bottoms=new LogicalScalar[Count];
            for(var Index=0;Index<Count;Index++){
                var Bottom=LogicalScalar.MaxValue;
                foreach(var Vertex in Asset.Meshes[Index].Vertices){
                    var Height=Vertex.U+Asset.Fragments[Index].LocalCenter.U;
                    if(Height<Bottom){ Bottom=Height; }
                }
                Bottoms[Index]=Bottom;
                if(Bottom<Lowest){ Lowest=Bottom; }
            }
            var Reach=Lowest+LogicalScalar.FromSingle(0.01f);
            for(var Index=0;Index<Count;Index++){ this.Anchored[Index]=Bottoms[Index]<=Reach; }
        }
        this.PieceOfFragment=new int[Count];
        this.Distance=new int[Count];
        var Whole=this.CreatePiece([.. Enumerable.Range(0,Count)],-1);
        this.LivePieces.Add(Whole.Id,Whole);
    }
    public PrefractureAsset Asset { get; }
    public FractureSettings Settings { get; }
    /// <summary>ボンドは切れておらず、破片も削除されていない。破壊可能オブジェクトは設定されたボディのまま。</summary>
    public bool IsWhole=>this.LivePieces.Count==1&&this.LivePieces.ContainsKey(0);
    public int BrokenBondCount { get;private set; }
    public bool IsBondBroken(int Bond)=>this.Broken[Bond];
    /// <summary>各階層でクラスター内部にあるボンドの強度（N·s）。クラスター倍率を適用した値。</summary>
    public LogicalScalar StrengthOf(int Bond)=>this.Strength[Bond];
    /// <summary>このティック中にボンドが受けたひずみ。</summary>
    public LogicalScalar StrainOf(int Bond)=>this.Strain[Bond];
    /// <summary>過去のティックからボンドが持ち越した内部ひずみ（M0124。切断後はゼロ）。</summary>
    public LogicalScalar InternalStrainOf(int Bond)=>this.Internal[Bond];
    public bool IsAnchored(int Fragment)=>this.Anchored[Fragment];
    /// <summary>ID 順に並んだ生存中の破片。</summary>
    public IEnumerable<FracturePiece>Pieces=>this.LivePieces.Values;
    public int PieceCount=>this.LivePieces.Count;
    /// <summary>この破片を含む生存中の破片。破片が削除済みなら null。</summary>
    public FracturePiece?PieceOf(int Fragment)=>this.LivePieces.TryGetValue(this.PieceOfFragment[Fragment],out var Piece)?Piece:null;
    public bool TryGetPiece(int Id,out FracturePiece Piece)=>this.LivePieces.TryGetValue(Id,out Piece!);
    /// <summary>
    /// 元形状の座標枠にある点を含む、<paramref name="Piece"/> の破片。ボロノイサイトが最も近い破片を選ぶ（調理済みの破片はサイトのセルと完全に一致する）。バージョン1のアセットには
    /// サイトがないため、代わりに中心が最も近い破片を選ぶ。同距離ならインデックスが小さい方を選ぶ。
    /// </summary>
    public int FragmentAt(LogicalVector3 SourcePoint,FracturePiece Piece){
        ArgumentNullException.ThrowIfNull(Piece);
        var Best=-1;
        var BestDistance=LogicalScalar.Zero;
        foreach(var Fragment in Piece.Fragments){
            var Site=this.Asset.Sites.Count==0?this.Asset.Fragments[Fragment].LocalCenter:this.Asset.Sites[Fragment];
            var Distance=(Site-SourcePoint).LengthSquared;
            if(Best<0||Distance<BestDistance){ Best=Fragment;BestDistance=Distance; }
        }
        return Best;
    }
    /// <summary>
    /// <paramref name="Fragment"/> に力積（N·s）が当たったことを記録する。未切断ボンドは全量を受け取り、未切断ボンドでつながる破片の隣接輪ごとに、その直前の輪の力積へ
    /// <see cref="FractureSettings.DamagePropagation"/> を掛けた量を受け取る（最大4輪）。値が0以下の場合、または破片が削除済みの場合は何も加えない。
    /// <paramref name="Lasting"/> が有効な場合（M0124）は衝撃として、ティック終了時にボンドの内部ひずみにも加算する。持続的な荷重（破壊可能オブジェクト上に載っているボディ）は
    ///  false を指定し、そのティック分のひずみだけを加える。
    /// </summary>
    public void AddImpulse(int Fragment,LogicalScalar Impulse,bool Lasting=true){
        if(Impulse<=LogicalScalar.Zero||(uint)Fragment>=(uint)this.PieceOfFragment.Length||this.PieceOf(Fragment) is null){ return; }
        Array.Fill(this.Distance,-1);
        this.Distance[Fragment]=0;
        Queue<int>Frontier=new();
        Frontier.Enqueue(Fragment);
        while(Frontier.Count>0){
            var Current=Frontier.Dequeue();
            if(this.Distance[Current]>=MaximumRings){ continue; }
            foreach(var Bond in this.FragmentBonds[Current]){
                if(this.Broken[Bond]){ continue; }
                var Value=this.Asset.Bonds[Bond];
                var Other=Value.FirstFragment==Current?Value.SecondFragment:Value.FirstFragment;
                if(this.Distance[Other]>=0){ continue; }
                this.Distance[Other]=this.Distance[Current]+1;
                Frontier.Enqueue(Other);
            }
        }
        var Weights=new LogicalScalar[MaximumRings+1];
        Weights[0]=Impulse;
        for(var Ring=1;Ring<=MaximumRings;Ring++){ Weights[Ring]=Weights[Ring-1]*this.Settings.DamagePropagation; }
        for(var Bond=0;Bond<this.Strain.Length;Bond++){
            if(this.Broken[Bond]){ continue; }
            var Value=this.Asset.Bonds[Bond];
            var First=this.Distance[Value.FirstFragment];
            var Second=this.Distance[Value.SecondFragment];
            if(First<0&&Second<0){ continue; }
            var Ring=First<0?Second:Second<0?First:Math.Min(First,Second);
            if(Ring>MaximumRings||Weights[Ring].IsZero){ continue; }
            this.Strain[Bond]+=Weights[Ring];
            if(Lasting){ this.Lasting[Bond]+=Weights[Ring]; }
        }
    }
    /// <summary>1つのボンドにひずみ（N·s）を直接加える。<paramref name="Lasting"/> の扱いは <see cref="AddImpulse"/> と同じ。</summary>
    public void AddStrain(int Bond,LogicalScalar Value,bool Lasting=true){
        if((uint)Bond>=(uint)this.Strain.Length||this.Broken[Bond]||Value<=LogicalScalar.Zero){ return; }
        this.Strain[Bond]+=Value;
        if(Lasting){ this.Lasting[Bond]+=Value; }
    }
    /// <summary>
    /// ひずみフィールド（UE の External Strain field、M0113）。2つの破片の中心間にある各未切断ボンドの中点が、<paramref name="SourceCenter"/> か
    /// ら <paramref name="Radius"/> 以内にあれば、<paramref name="Value"/> のひずみを加える。ひずみは半径に向かって線形に弱まり、半径上でゼロになる。
    /// フィールドは衝撃として持続する。
    /// </summary>
    public void AddStrainSphere(LogicalVector3 SourceCenter,LogicalScalar Radius,LogicalScalar Value){
        if(Radius<=LogicalScalar.Zero||Value<=LogicalScalar.Zero){ return; }
        var Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
        for(var Bond=0;Bond<this.Strain.Length;Bond++){
            if(this.Broken[Bond]){ continue; }
            var Pair=this.Asset.Bonds[Bond];
            if(this.PieceOf(Pair.FirstFragment) is null){ continue; }
            var Middle=(this.Asset.Fragments[Pair.FirstFragment].LocalCenter+this.Asset.Fragments[Pair.SecondFragment].LocalCenter)*Half;
            var DistanceSquared=(Middle-SourceCenter).LengthSquared;
            if(DistanceSquared>Radius*Radius){ continue; }
            if(!LogicalScalar.TrySquareRoot(DistanceSquared,out var Distance)){ continue; }
            var Added=Value*(LogicalScalar.One-Distance/Radius);
            this.Strain[Bond]+=Added;
            this.Lasting[Bond]+=Added;
        }
    }
    /// <summary>
    /// ティックを終了する。内部ひずみとこのティックのひずみの合計が強度以上になった未切断ボンドを、ボンド順にすべて切り、それぞれを <paramref name="Breaks"/> に追加する。この
    /// ティックの持続ひずみを未切断ボンドの内部ひずみに反映する（<see cref="Carry"/> を参照）。衝撃で内部ひずみを増やした各ボンドと持ち越し値を
    /// <paramref name="Raised"/> に追加する（物理キャッシュが保持するため、再生時に同じ値を復元できる）。ティックのひずみをクリアし、分離した破片を分割する。
    /// </summary>
    public FractureStep Resolve(List<FractureBondBreak>Breaks,List<FractureBondStrain>?Raised=null){
        ArgumentNullException.ThrowIfNull(Breaks);
        var Start=Breaks.Count;
        for(var Bond=0;Bond<this.Strain.Length;Bond++){
            if(this.Broken[Bond]){ continue; }
            var Total=this.Internal[Bond]+this.Strain[Bond];
            if(Total<this.Strength[Bond]||Total.IsZero){ continue; }
            Breaks.Add(new FractureBondBreak(Bond,Total));
        }
        List<int>Bonds=[];
        for(var Index=Start;Index<Breaks.Count;Index++){ Bonds.Add(Breaks[Index].Bond); }
        var Result=Bonds.Count==0?FractureStep.None:this.Apply(Bonds);
        this.Carry(Raised);
        Array.Clear(this.Strain);
        Array.Clear(this.Lasting);
        return Result;
    }
    /// <summary>
    /// 次のティックへ持ち越す、すべての未切断ボンドの内部ひずみ。各値は（内部ひずみ + このティックの持続ひずみ）×
    /// <see cref="FractureSettings.StrainRetention"/> とする。ボンド強度の100万分の1（2⁻²⁰）を下回るとゼロにする（古い衝撃の影響を無限に縮小させ続けず、
    /// 終了させる）。切断済みボンドはひずみを持たない。
    /// </summary>
    private void Carry(List<FractureBondStrain>?Raised){
        var Fraction=LogicalScalar.One/LogicalScalar.FromInt64(1<<20);
        for(var Bond=0;Bond<this.Internal.Length;Bond++){
            if(this.Broken[Bond]){
                this.Internal[Bond]=LogicalScalar.Zero;
                continue;
            }
            var Next=(this.Internal[Bond]+this.Lasting[Bond])*this.Settings.StrainRetention;
            if(Next<this.Strength[Bond]*Fraction){ Next=LogicalScalar.Zero; }
            this.Internal[Bond]=Next;
            if(Raised is not null&&this.Lasting[Bond]>LogicalScalar.Zero&&Next>LogicalScalar.Zero){ Raised.Add(new FractureBondStrain(Bond,Next)); }
        }
    }
    /// <summary>
    /// 記録したティックを再生する（M0124）。まず <see cref="Apply"/> と同じ手順で記録済みボンドを切る。新たなひずみを加えずに未切断ボンドの内部ひずみを持ち越し、記録で増加したボンド
    /// には記録済みの値を設定する。これにより内部ひずみは通常実行時と同じ状態になり、再生後に通常実行へ戻っても同じように破壊される。
    /// </summary>
    public FractureStep Replay(IReadOnlyList<int>Bonds,IReadOnlyList<FractureBondStrain>Raised){
        ArgumentNullException.ThrowIfNull(Bonds);
        ArgumentNullException.ThrowIfNull(Raised);
        var Result=Bonds.Count==0?FractureStep.None:this.Apply(Bonds);
        Array.Clear(this.Strain);
        Array.Clear(this.Lasting);
        this.Carry(null);
        foreach(var Record in Raised){
            if((uint)Record.Bond<(uint)this.Internal.Length&&!this.Broken[Record.Bond]&&Record.Strain>=LogicalScalar.Zero){ this.Internal[Record.Bond]=Record.Strain; }
        }
        return Result;
    }
    /// <summary>指定されたボンドを切り（記録済みティックの再生、またはゲーム側の選択）、分離した破片を分割する。同じボンドを切れば、<see cref="Resolve"/> と同じ破片になる。切断済みまたは不明なボンドは無視する。</summary>
    public FractureStep Apply(IReadOnlyList<int>Bonds){
        ArgumentNullException.ThrowIfNull(Bonds);
        SortedSet<int>Touched=[];
        foreach(var Bond in Bonds){
            if((uint)Bond>=(uint)this.Broken.Length||this.Broken[Bond]){ continue; }
            this.Broken[Bond]=true;
            this.BrokenBondCount++;
            var Value=this.Asset.Bonds[Bond];
            if(this.PieceOf(Value.FirstFragment) is { }FirstPiece){ Touched.Add(FirstPiece.Id); }
            if(this.PieceOf(Value.SecondFragment) is { }SecondPiece){ Touched.Add(SecondPiece.Id); }
        }
        List<FracturePiece>Ended=[];
        List<FracturePiece>Began=[];
        foreach(var Id in Touched){
            var Piece=this.LivePieces[Id];
            var Parts=this.Components(Piece);
            if(Parts.Count<2){ continue; }
            this.LivePieces.Remove(Id);
            Ended.Add(Piece);
            foreach(var Part in Parts){
                var Child=this.CreatePiece(Part,Piece.Id);
                this.LivePieces.Add(Child.Id,Child);
                Began.Add(Child);
            }
        }
        return Ended.Count==0?FractureStep.None:new FractureStep(Ended,Began);
    }
    /// <summary>破片を削除する（寿命切れ、または破片数の上限到達）。その破片には以後ひずみを加えず、どの破片にも属さない状態にする。</summary>
    public bool Remove(int PieceId)=>this.LivePieces.Remove(PieceId);
    /// <summary>
    /// 破片のボディ座標枠（原点は <see cref="FracturePiece.Center"/>）における衝突形状。破片1つだけのボディには、その破片固有の凸多面体を使う。複数の破片からなる場合は、そ
    /// れぞれの多面体を結合した形状を使う（UE の cluster union における子形状の結合）。破片の組み合わせごとに一度だけ作成する。
    /// </summary>
    public PhysicsShapeAsset ShapeOf(FracturePiece Piece){
        ArgumentNullException.ThrowIfNull(Piece);
        if(Piece.Fragments.Count==1){ return this.Asset.CollisionShapes[Piece.Fragments[0]]; }
        var Key=PieceKey(Piece);
        if(this.Shapes.TryGetValue(Key,out var Existing)){ return Existing; }
        List<PhysicsShapeAsset>Children=[];
        foreach(var Fragment in Piece.Fragments){
            var Offset=this.Asset.Fragments[Fragment].LocalCenter-Piece.Center;
            var Mesh=this.Asset.Meshes[Fragment];
            Children.Add(PhysicsShapeAsset.CreateConvexPolyhedron($"{this.Asset.Fragments[Fragment].ShapeId}@{Key}",[.. Mesh.Vertices.Select(Vertex=>Vertex+Offset)],Mesh.Indices));
        }
        var Compound=PhysicsShapeAsset.CreateCompound(Key,Children);
        this.Shapes.Add(Key,Compound);
        return Compound;
    }
    /// <summary>このアセットの破片集合を識別する安定した名前。アセットのソースハッシュと、破片インデックス列の FNV-1a ハッシュから生成する。</summary>
    public string PieceKey(FracturePiece Piece){
        ArgumentNullException.ThrowIfNull(Piece);
        var Hash=14695981039346656037UL;
        foreach(var Fragment in Piece.Fragments){ Hash=(Hash^(ulong)(uint)Fragment)*1099511628211UL; }
        var Source=this.Asset.SourceHash.Length>16?this.Asset.SourceHash[..16]:this.Asset.SourceHash;
        return $"{Source}.piece.{Piece.Fragments.Count}.{Hash:x16}";
    }
    private FracturePiece CreatePiece(int[]Fragments,int Parent){
        var Mass=LogicalScalar.Zero;
        var Weighted=LogicalVector3.Zero;
        var Anchored=false;
        foreach(var Fragment in Fragments){
            var Value=this.Asset.Fragments[Fragment];
            Mass+=Value.Mass;
            Weighted+=Value.LocalCenter*Value.Mass;
            Anchored|=this.Anchored[Fragment];
        }
        var Piece=new FracturePiece(this.NextPiece++,Fragments,Anchored,Weighted/Mass,Mass,Parent);
        foreach(var Fragment in Fragments){ this.PieceOfFragment[Fragment]=Piece.Id; }
        return Piece;
    }
    /// <summary>未切断ボンドを通じて連結する破片の各部分。各部分は昇順に並び、最小の破片番号で順序付ける。</summary>
    private List<int[]>Components(FracturePiece Piece){
        HashSet<int>Members=[.. Piece.Fragments];
        HashSet<int>Seen=[];
        List<int[]>Parts=[];
        foreach(var Start in Piece.Fragments){
            if(!Seen.Add(Start)){ continue; }
            List<int>Part=[Start];
            Queue<int>Frontier=new();
            Frontier.Enqueue(Start);
            while(Frontier.Count>0){
                var Current=Frontier.Dequeue();
                foreach(var Bond in this.FragmentBonds[Current]){
                    if(this.Broken[Bond]){ continue; }
                    var Value=this.Asset.Bonds[Bond];
                    var Other=Value.FirstFragment==Current?Value.SecondFragment:Value.FirstFragment;
                    if(!Members.Contains(Other)||!Seen.Add(Other)){ continue; }
                    Part.Add(Other);
                    Frontier.Enqueue(Other);
                }
            }
            Part.Sort();
            Parts.Add([.. Part]);
        }
        return Parts;
    }
}
