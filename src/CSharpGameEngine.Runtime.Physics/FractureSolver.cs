namespace CSharpGameEngine.Runtime.Physics;
/// <summary>
/// The cooked prefracture assets a process has loaded (ADR-0078/M0113), by full path, shared by the destruction plugin and the renderer (an asset is
/// immutable). A file is read again when its length or write time changes. <see cref="Register"/> puts an asset made in memory under a path (tests and
/// previews); it wins over the file until <see cref="Forget"/>.
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
/// How a destructible breaks at run time (ADR-0078/M0113). <see cref="ClusterStrengthScale"/> multiplies the strength of a bond inside one cluster of the
/// cooked hierarchy (UE's per-level damage threshold: a hit first frees whole clusters, and a harder one breaks them up). <see cref="DamagePropagation"/> is the
/// share of an impulse that reaches the bonds one fragment further away (UE's damage propagation factor), applied ring by ring.
/// </summary>
public readonly record struct FractureSettings(LogicalScalar ClusterStrengthScale,LogicalScalar DamagePropagation,bool AnchorBottom);
/// <summary>A bond that broke in a step, and the strain that broke it.</summary>
public readonly record struct FractureBondBreak(int Bond,LogicalScalar Strain);
/// <summary>
/// One piece of a destructible: a connected set of fragments that moves as one body (UE's cluster union). <see cref="Center"/> is its centre of mass in the
/// source's frame, the origin of its body. Piece 0 is the whole object, which is the destructible's own authored body until it first splits.
/// </summary>
public sealed class FracturePiece(int Id,int[]Fragments,bool Anchored,LogicalVector3 Center,LogicalScalar Mass,int Parent) {
    public int Id { get; }=Id;
    /// <summary>The fragments, ascending.</summary>
    public IReadOnlyList<int>Fragments { get; }=Fragments;
    /// <summary>Whether a fragment of the piece is anchored: the piece is held still (a static body).</summary>
    public bool Anchored { get; }=Anchored;
    public LogicalVector3 Center { get; }=Center;
    public LogicalScalar Mass { get; }=Mass;
    /// <summary>The piece it split from (its body's motion carries over); -1 for the whole object.</summary>
    public int Parent { get; }=Parent;
    public bool IsWhole=>this.Id==0;
}
/// <summary>What a step changed: the pieces that ended (split or removed) and the pieces that began, in the order their bodies are made.</summary>
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
/// The breaking state of one destructible (ADR-0078/M0113): the solver of UE's Chaos clustering, kept free of the ECS so the cook, the runtime and the tests
/// share it. Strain (an impulse, N·s) is gathered on the bonds during a tick from contact impulses (<see cref="AddImpulse"/>: the hit fragment's bonds take
/// all of it, the next ring <see cref="FractureSettings.DamagePropagation"/> of it, and so on) and from strain fields (<see cref="AddStrainSphere"/>).
/// <see cref="Resolve"/> breaks every bond whose strain reached its strength (equal breaks), forgets the tick's strain (a resting load does not wear a bond
/// down), and splits each piece that no longer holds together into its connected parts. Anchored fragments (<see cref="FractureSettings.AnchorBottom"/>:
/// those on the source's lowest face) keep their pieces still. Everything is integer or Q64.64 and in index order, so the same input gives the same pieces.
/// </summary>
public sealed class FractureState {
    private const int MaximumRings=4;
    private readonly LogicalScalar[]Strength;
    private readonly LogicalScalar[]Strain;
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
        if(Settings.ClusterStrengthScale<=LogicalScalar.Zero||Settings.DamagePropagation<LogicalScalar.Zero||Settings.DamagePropagation>LogicalScalar.One){
            throw new ArgumentOutOfRangeException(nameof(Settings),"The cluster strength scale must be positive and the damage propagation between 0 and 1.");
        }
        this.Asset=Asset;
        this.Settings=Settings;
        var Count=Asset.Fragments.Count;
        this.Strength=new LogicalScalar[Asset.Bonds.Count];
        this.Strain=new LogicalScalar[Asset.Bonds.Count];
        this.Broken=new bool[Asset.Bonds.Count];
        List<int>[]Lists=new List<int>[Count];
        for(var Index=0;Index<Count;Index++){ Lists[Index]=[]; }
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
            var Value=Asset.Bonds[Bond];
            this.Strength[Bond]=Asset.ClusterOf(Value.FirstFragment)==Asset.ClusterOf(Value.SecondFragment)?Value.Strength*Settings.ClusterStrengthScale:Value.Strength;
            Lists[Value.FirstFragment].Add(Bond);
            Lists[Value.SecondFragment].Add(Bond);
        }
        this.FragmentBonds=[.. Lists.Select(static List=>List.ToArray())];
        this.Anchored=new bool[Count];
        if(Settings.AnchorBottom){
            // The fragments that reach the source's lowest point (within 1 cm) stand on what the source stood on.
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
    /// <summary>No bond has broken and no piece was removed: the destructible is still its own authored body.</summary>
    public bool IsWhole=>this.LivePieces.Count==1&&this.LivePieces.ContainsKey(0);
    public int BrokenBondCount { get;private set; }
    public bool IsBondBroken(int Bond)=>this.Broken[Bond];
    /// <summary>The strength (N·s) of a bond, with the cluster scale applied.</summary>
    public LogicalScalar StrengthOf(int Bond)=>this.Strength[Bond];
    /// <summary>The strain a bond has gathered in the current tick.</summary>
    public LogicalScalar StrainOf(int Bond)=>this.Strain[Bond];
    public bool IsAnchored(int Fragment)=>this.Anchored[Fragment];
    /// <summary>The live pieces, by id.</summary>
    public IEnumerable<FracturePiece>Pieces=>this.LivePieces.Values;
    public int PieceCount=>this.LivePieces.Count;
    /// <summary>The live piece of a fragment, or null when its piece was removed.</summary>
    public FracturePiece?PieceOf(int Fragment)=>this.LivePieces.TryGetValue(this.PieceOfFragment[Fragment],out var Piece)?Piece:null;
    public bool TryGetPiece(int Id,out FracturePiece Piece)=>this.LivePieces.TryGetValue(Id,out Piece!);
    /// <summary>
    /// The fragment of <paramref name="Piece"/> that holds a point of the source's frame: the one whose Voronoi site is nearest (a cooked fragment is exactly
    /// its site's cell). A version-1 asset has no sites, so the nearest centre stands in. Ties go to the lower index.
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
    /// Adds an impulse (N·s) that hit <paramref name="Fragment"/>: its intact bonds take all of it, and each further ring of fragments through intact bonds
    /// takes <see cref="FractureSettings.DamagePropagation"/> times the ring before (at most four rings). Zero, negative or a removed fragment adds nothing.
    /// </summary>
    public void AddImpulse(int Fragment,LogicalScalar Impulse){
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
        }
    }
    /// <summary>Adds strain (N·s) to one bond directly.</summary>
    public void AddStrain(int Bond,LogicalScalar Value){
        if((uint)Bond>=(uint)this.Strain.Length||this.Broken[Bond]||Value<=LogicalScalar.Zero){ return; }
        this.Strain[Bond]+=Value;
    }
    /// <summary>
    /// A strain field (UE's External Strain field, M0113): every intact bond whose middle (between its two fragments' centres) lies within
    /// <paramref name="Radius"/> of <paramref name="SourceCenter"/> takes <paramref name="Value"/> scaled down linearly to zero at the radius.
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
            this.Strain[Bond]+=Value*(LogicalScalar.One-Distance/Radius);
        }
    }
    /// <summary>
    /// Ends the tick: breaks (in bond order) every intact bond whose strain reached its strength, adding each to <paramref name="Breaks"/>, clears the strain,
    /// and splits the pieces that came apart.
    /// </summary>
    public FractureStep Resolve(List<FractureBondBreak>Breaks){
        ArgumentNullException.ThrowIfNull(Breaks);
        var Start=Breaks.Count;
        for(var Bond=0;Bond<this.Strain.Length;Bond++){
            if(this.Broken[Bond]||this.Strain[Bond]<this.Strength[Bond]||this.Strain[Bond].IsZero){ continue; }
            Breaks.Add(new FractureBondBreak(Bond,this.Strain[Bond]));
        }
        Array.Clear(this.Strain);
        if(Breaks.Count==Start){ return FractureStep.None; }
        List<int>Bonds=[];
        for(var Index=Start;Index<Breaks.Count;Index++){ Bonds.Add(Breaks[Index].Bond); }
        return this.Apply(Bonds);
    }
    /// <summary>
    /// Breaks the listed bonds (a recorded tick being played back, or a game's own choice) and splits the pieces that came apart; the same bonds give the same
    /// pieces as <see cref="Resolve"/>. Broken and unknown bonds are ignored.
    /// </summary>
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
    /// <summary>Removes a piece (its time ran out or the fragment budget was spent): its fragments take no more strain and belong to no piece.</summary>
    public bool Remove(int PieceId)=>this.LivePieces.Remove(PieceId);
    /// <summary>
    /// The collision shape of a piece in its body's frame (origin at <see cref="FracturePiece.Center"/>): the fragment's own convex polyhedron for a piece of
    /// one fragment, otherwise a compound of its fragments' polyhedra (UE's cluster union of the children's shapes). Made once per fragment set.
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
    /// <summary>A stable name for a fragment set of this asset: the asset's source hash and an FNV-1a hash of the fragment indices.</summary>
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
    /// <summary>The connected parts of a piece through its intact bonds, each ascending, ordered by their lowest fragment.</summary>
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
