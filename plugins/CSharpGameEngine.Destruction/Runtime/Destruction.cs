using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Destruction;
/// <summary>
/// The destructibles of a game (ADR-0078/M0113). System の引数に <c>Destruction</c> と書くと、同じゲームの全 System が同じ 1 つを受ける。It owns every
/// destructible's breaking state (<see cref="FractureState"/>, the solver of <c>Runtime.Physics</c>) and the Entities it made for it: one drawn fragment per
/// cooked fragment, and one body per piece once the object has come apart. A game can strain a destructible itself (<see cref="ApplyStrain"/>: a shot,
/// an explosion) or hit it (<see cref="ApplyImpulse"/>); the strain is resolved in the same fixed tick, after the physics step.
/// Everything is processed in the order of the destructibles' physics stable IDs, so a run is the same every time.
/// </summary>
[EngineService]
public sealed class Destruction {
    internal sealed class Instance(Entity Root,ulong RootStableId,string AssetPath,FractureState State,Destructible Settings,Collider Collider,bool Dynamic,RigidBody Body) {
        internal readonly Entity Root=Root;
        internal readonly ulong RootStableId=RootStableId;
        internal readonly string AssetPath=AssetPath;
        internal readonly FractureState State=State;
        internal readonly Destructible Settings=Settings;
        internal readonly Collider Collider=Collider;
        internal readonly bool Dynamic=Dynamic;
        internal readonly RigidBody Body=Body;
        internal readonly Entity[]Visuals=new Entity[State.Asset.Fragments.Count];
        internal readonly Dictionary<int,Entity>Bodies=[];
        /// <summary>The loose pieces in the order they came free, with the tick they did.</summary>
        internal readonly List<(int Piece,ulong Tick)>Loose=[];
    }
    private readonly SortedDictionary<ulong,Instance>Instances=[];
    private readonly Dictionary<Entity,(Instance Instance,int Piece)>Owners=[];
    private readonly List<(Entity Body,LogicalVector3 Point,LogicalScalar Impulse)>PendingImpulses=[];
    private readonly List<(LogicalVector3 Center,LogicalScalar Radius,LogicalScalar Strain)>PendingStrain=[];
    private readonly Dictionary<Entity,HashSet<ulong>>Pushed=[];
    private readonly List<PhysicsContactManifold>Manifolds=[];
    private readonly List<FractureBondBreak>Breaks=[];
    private readonly List<FractureBondStrain>Raised=[];
    private readonly List<int>RecordedBonds=[];
    private readonly List<FractureBondStrain>RecordedStrains=[];
    private readonly HashSet<ulong>BeganPairs=[];
    /// <summary>The last tick each character touched each body (M0124): a character's hit lasts only in the tick its touch began, as a contact's does.</summary>
    private readonly Dictionary<(Entity Character,Entity Body),ulong>CharacterTouches=[];
    private readonly List<(Entity Character,Entity Body)>StaleTouches=[];
    private readonly List<(Entity Body,LogicalVector3 Point,LogicalScalar Impulse,bool Lasting)>PendingHits=[];
    private readonly List<ulong>Ended=[];
    private uint Sequence;
    /// <summary>How many destructibles are loaded.</summary>
    public int Count=>this.Instances.Count;
    /// <summary>The breaking state of a loaded destructible (for the game and the tests).</summary>
    public bool TryGetState(Entity Root,out FractureState State){
        foreach(var Instance in this.Instances.Values){
            if(Instance.Root==Root){
                State=Instance.State;
                return true;
            }
        }
        State=null!;
        return false;
    }
    /// <summary>The Entity drawing a fragment of a destructible, or <see cref="Entity"/>'s default when it was removed.</summary>
    public Entity FragmentEntity(Entity Root,int Fragment){
        foreach(var Instance in this.Instances.Values){
            if(Instance.Root==Root&&(uint)Fragment<(uint)Instance.Visuals.Length){ return Instance.Visuals[Fragment]; }
        }
        return default;
    }
    /// <summary>The body of a live piece (the destructible itself while it is whole).</summary>
    public Entity PieceBody(Entity Root,int Piece){
        foreach(var Instance in this.Instances.Values){
            if(Instance.Root!=Root){ continue; }
            if(Piece==0&&Instance.State.IsWhole){ return Root; }
            return Instance.Bodies.TryGetValue(Piece,out var Body)?Body:default;
        }
        return default;
    }
    /// <summary>Whether an Entity is a destructible (whole) or the body of one of its pieces.</summary>
    public bool Owns(Entity Body)=>this.Owners.ContainsKey(Body);
    /// <summary>
    /// Strains every bond whose middle lies within <paramref name="Radius"/> of the world point <paramref name="Center"/> with <paramref name="Strain"/> (N·s),
    /// less with distance down to nothing at the radius (UE's External Strain field). Resolved at this fixed tick's destruction step.
    /// </summary>
    public void ApplyStrain(LogicalVector3 Center,LogicalScalar Radius,LogicalScalar Strain){
        if(Radius>LogicalScalar.Zero&&Strain>LogicalScalar.Zero){ this.PendingStrain.Add((Center,Radius,Strain)); }
    }
    /// <summary>
    /// Hits a destructible (or a piece's body) at a world point with an impulse (N·s): the fragment there and its neighbours take the strain. A hit is an
    /// impact, so it also adds to the bonds' internal strain (M0124).
    /// </summary>
    public void ApplyImpulse(Entity Body,LogicalVector3 Point,LogicalScalar Impulse){
        if(Impulse>LogicalScalar.Zero&&this.Owners.ContainsKey(Body)){ this.PendingImpulses.Add((Body,Point,Impulse)); }
    }
    /// <summary>
    /// A character's hit (<see cref=DestructionImpact/>, M0124): an impact only in the tick the character's touch of that body began; while the character
    /// keeps pressing into it, tick after tick, the hit strains that tick only, as a contact that goes on does (a character leaning into a crate does not
    /// wear it down tick by tick).
    /// </summary>
    internal void ApplyCharacterImpulse(Entity Character,Entity Body,LogicalVector3 Point,LogicalScalar Impulse,ulong Tick){
        if(Impulse<=LogicalScalar.Zero||!this.Owners.ContainsKey(Body)){ return; }
        var Began=!(this.CharacterTouches.TryGetValue((Character,Body),out var Last)&&Last+1>=Tick);
        this.CharacterTouches[(Character,Body)]=Tick;
        this.PendingHits.Add((Body,Point,Impulse,Began));
    }
    /// <summary>Puts an asset made in memory where the destructibles look for <paramref name="Name"/> (tests and previews; the build cooks the real ones).</summary>
    public static void Register(string Name,PrefractureAsset Asset)=>PrefractureAssetCache.Register(ContentRoot.Resolve(DestructionDefaults.AssetPath(Name)),Asset);
    /// <summary>The stable ID of a piece's body: an FNV-1a hash of the destructible's ID and the piece number, with the top bit set so it never meets an Entity-number ID.</summary>
    public static ulong PieceStableId(ulong RootStableId,int Piece){
        var Hash=14695981039346656037UL;
        Hash=(Hash^RootStableId)*1099511628211UL;
        Hash=(Hash^0xf7ac7UL)*1099511628211UL;
        Hash=(Hash^(ulong)(uint)Piece)*1099511628211UL;
        return Hash|(1UL<<63);
    }
    /// <summary>
    /// Loads a destructible the first time it is seen: the cooked asset, its breaking state and one drawn fragment Entity per fragment, which follow the
    /// destructible until it breaks.
    /// </summary>
    internal void Load(Entity Root,in Transform Transform,in Destructible Settings,ref DestructibleState Runtime,ref State State){
        Runtime.Attempted=true;
        if(!DestructionDefaults.IsValidName(Settings.FractureAsset)){
            this.Fail(ref Runtime,$"DST001: The Destructible of {Root} names no valid FractureAsset ('{Settings.FractureAsset}': letters, digits, '-', '_' and '.').");
            return;
        }
        if(Transform.Scale!=LogicalVector3.One){
            this.Fail(ref Runtime,$"DST004: The Destructible of {Root} has a scale; give the size with its Collider and keep the scale at one.");
            return;
        }
        var Path=DestructionDefaults.AssetPath(Settings.FractureAsset);
        if(!PrefractureAssetCache.TryGet(ContentRoot.Resolve(Path),out var Asset,out var Error)){
            this.Fail(ref Runtime,"DST002: "+Error);
            return;
        }
        var Dynamic=State.World.TryGetComponent(Root,out RigidBody Body)&&Body.MotionType==PhysicsMotionType.Dynamic;
        FractureState Fracture;
        try {
            var Retention=FractureSettings.RetentionFor(Settings.StrainDecay,State.FixedTickRate>0?State.FixedTickRate:60);
            Fracture=new FractureState(Asset,new FractureSettings(
                Settings.ClusterStrengthScale,Settings.DamagePropagation,Settings.Anchor==DestructibleAnchor.Bottom&&!Dynamic,Retention));
        }
        catch(ArgumentOutOfRangeException Exception){
            this.Fail(ref Runtime,$"DST003: The Destructible of {Root} has unusable settings: {Exception.Message}");
            return;
        }
        State.World.TryGetComponent(Root,out Collider Collider);
        var StableId=PhysicsDomainRuntime.ResolveStableId(Root,State.World);
        var Instance=new Instance(Root,StableId,Path,Fracture,Settings,Collider,Dynamic,Body);
        if(!this.Instances.TryAdd(StableId,Instance)){
            this.Fail(ref Runtime,$"DST005: Two destructibles share the physics stable ID {StableId}.");
            return;
        }
        this.Owners[Root]=(Instance,0);
        var Rotation=CurrentRotation(Root,State.World,Transform.Rotation);
        for(var Fragment=0;Fragment<Asset.Fragments.Count;Fragment++){
            var Offset=Asset.Fragments[Fragment].LocalCenter;
            Instance.Visuals[Fragment]=State.World.CreateDeferred(
                new Transform { Frame=Transform.Frame,Position=Transform.Position+Rotate(Rotation,Offset),Rotation=Rotation,Scale=LogicalVector3.One },
                new FragmentMesh { AssetPath=Path,Fragment=Fragment,OuterColor=Settings.OuterColor,InteriorColor=Settings.InteriorColor },
                new DestructibleFragment { Root=Root,Fragment=Fragment,Body=Root,Offset=Offset });
        }
        Runtime.Loaded=true;
        Runtime.Fragments=Asset.Fragments.Count;
        Runtime.Pieces=1;
    }
    /// <summary>
    /// The destruction step of one fixed tick, after the physics step. Live: gathers the tick's strain (contact impulses on destructibles and their pieces,
    /// the game's impulses and strain fields), breaks the bonds whose strain (with their internal strain, M0124) reached their strength, reports each as a
    /// <c>BondBroken</c> event and a cache record, records the bonds whose internal strain an impact raised (the cache keeps them), and turns the parts that
    /// came apart into bodies. Impacts are what last: a contact in the tick it began, the game's impulses (a character's hits come this way) and fields; a
    /// contact that goes on (a body resting on the object) strains the tick only. Playing back a cache: breaks exactly the bonds the cache recorded for this
    /// tick and restores the recorded internal strains (and ignores strain), so the same pieces appear and a live run that follows goes on as the recorded
    /// one did. Then removes the loose pieces whose time ran out or that exceed the fragment budget.
    /// </summary>
    internal void Step(IPhysicsHost Host,ref State State){
        var Domain=Host.Physics.Active;
        var World=Domain.MutableWorld;
        var Tick=State.FixedTick;
        var Playback=Domain.CacheState==PhysicsCachePlaybackState.Playing;
        this.Ended.Clear();
        foreach(var (Id,Instance) in this.Instances){
            if(!State.World.IsAlive(Instance.Root)){ this.Ended.Add(Id); }
        }
        foreach(var Id in this.Ended){ this.Unload(this.Instances[Id],ref State); }
        if(!Playback){
            this.Manifolds.Clear();
            World.CollectContactManifolds(this.Manifolds);
            this.BeganPairs.Clear();
            foreach(var Event in World.Events){
                if(Event.Type==PhysicsEventType.ContactBegin){ this.BeganPairs.Add(Event.PairId); }
            }
            foreach(var Manifold in this.Manifolds){
                var Impact=this.BeganPairs.Contains(Manifold.PairId);
                foreach(var Side in (ReadOnlySpan<Entity>)[Manifold.First,Manifold.Second]){
                    if(!this.Owners.TryGetValue(Side,out var Owner)){ continue; }
                    foreach(var Point in Manifold.Points){ this.AddImpulse(Owner.Instance,Owner.Piece,Side,Point.Position,Point.AccumulatedNormalImpulse,Impact,World,ref State); }
                }
            }
            foreach(var (Body,Point,Impulse) in this.PendingImpulses){
                if(this.Owners.TryGetValue(Body,out var Owner)){ this.AddImpulse(Owner.Instance,Owner.Piece,Body,Point,Impulse,true,World,ref State); }
            }
            foreach(var (Body,Point,Impulse,Lasting) in this.PendingHits){
                if(this.Owners.TryGetValue(Body,out var Owner)){ this.AddImpulse(Owner.Instance,Owner.Piece,Body,Point,Impulse,Lasting,World,ref State); }
            }
            foreach(var (Center,Radius,Strain) in this.PendingStrain){
                foreach(var Instance in this.Instances.Values){ this.AddStrainSphere(Instance,Center,Radius,Strain,World,ref State); }
            }
        }
        this.PendingImpulses.Clear();
        this.PendingHits.Clear();
        this.PendingStrain.Clear();
        // Touches not renewed in this tick have ended.
        this.StaleTouches.Clear();
        foreach(var (Key,Last) in this.CharacterTouches){
            if(Last<Tick){ this.StaleTouches.Add(Key); }
        }
        foreach(var Key in this.StaleTouches){ this.CharacterTouches.Remove(Key); }
        foreach(var Instance in this.Instances.Values){
            FractureStep Result;
            if(Playback){
                this.RecordedBonds.Clear();
                foreach(var Break in World.BondBreaks){
                    if(Break.RootStableId==Instance.RootStableId){ this.RecordedBonds.Add(Break.Bond); }
                }
                this.RecordedStrains.Clear();
                foreach(var Strain in World.BondStrains){
                    if(Strain.RootStableId==Instance.RootStableId){ this.RecordedStrains.Add(new FractureBondStrain(Strain.Bond,Strain.Strain)); }
                }
                Result=Instance.State.Replay(this.RecordedBonds,this.RecordedStrains);
            }
            else {
                this.Breaks.Clear();
                this.Raised.Clear();
                Result=Instance.State.Resolve(this.Breaks,this.Raised);
                foreach(var Break in this.Breaks){ World.ReportBondBreak(Tick,Instance.Root,Instance.RootStableId,Break.Bond,Break.Strain); }
                foreach(var Strain in this.Raised){ World.ReportBondStrain(Instance.RootStableId,Strain.Bond,Strain.Strain); }
            }
            if(Result.Changed){ this.Split(Instance,Result,World,Tick,ref State); }
            this.Expire(Instance,Tick,ref State);
            if(State.World.TryGetComponent(Instance.Root,out DestructibleState Runtime)){
                Runtime.Broken=!Instance.State.IsWhole;
                Runtime.BrokenBonds=Instance.State.BrokenBondCount;
                Runtime.Pieces=Instance.State.PieceCount;
                Runtime.LoosePieces=Instance.Loose.Count;
                State.World.TrySetComponent(Instance.Root,in Runtime);
            }
        }
    }
    /// <summary>
    /// Pushes every moving body within the field's radius away from it once (<see cref="ForceField"/>): an impulse of the body's mass times the field's speed,
    /// less with distance, for the next tick.
    /// </summary>
    internal void Push(Entity Field,LogicalVector3 Center,in ForceField Settings,ref ForceFieldState Runtime,IPhysicsHost Host,ref State State){
        if(Settings.Radius<=LogicalScalar.Zero||Settings.Speed<=LogicalScalar.Zero){ return; }
        var Domain=Host.Physics.Active;
        if(!this.Pushed.TryGetValue(Field,out var Done)){
            Done=[];
            this.Pushed[Field]=Done;
        }
        List<PhysicsOverlapHit>Hits=[];
        var Reach=new LogicalVector3(Settings.Radius,Settings.Radius,Settings.Radius);
        Domain.World.Overlap(new PhysicsAabb(Center-Reach,Center+Reach),PhysicsLayerMask.All,Hits);
        Hits.Sort(static(Left,Right)=>Left.StableId.CompareTo(Right.StableId));
        foreach(var Hit in Hits){
            if(Done.Contains(Hit.StableId)||!Domain.World.TryGetBodyState(Hit.Entity,out var Body)||Body.MotionType!=PhysicsMotionType.Dynamic){ continue; }
            if(!State.World.TryGetComponent(Hit.Entity,out RigidBody Rigid)){ continue; }
            var Away=Body.Position-Center;
            var DistanceSquared=Away.LengthSquared;
            if(DistanceSquared>Settings.Radius*Settings.Radius||!LogicalScalar.TrySquareRoot(DistanceSquared,out var Distance)){ continue; }
            var Direction=Away.TryNormalize(out var Unit)?Unit:LogicalVector3.UnitU;
            var Speed=Settings.Speed*(LogicalScalar.One-Distance/Settings.Radius);
            Done.Add(Hit.StableId);
            Domain.Commands.Impulse(State.FixedTick+1,FieldSource(Field),this.Sequence++,Hit.Entity,Direction*(Speed*Rigid.Mass));
        }
        Runtime.Pushed=Done.Count;
    }
    /// <summary>Forgets the bodies a removed field pushed.</summary>
    internal void ForgetField(Entity Field)=>this.Pushed.Remove(Field);
    private static ulong FieldSource(Entity Field)=>(1UL<<62)|(ulong)(uint)Field.Index;
    /// <summary>
    /// The impulse at a world point of a body (the destructible itself, or a piece) goes to the fragment of that body that holds the point. <paramref name="Lasting"/>:
    /// an impact, which also adds to the internal strain.
    /// </summary>
    private void AddImpulse(Instance Instance,int Piece,Entity Body,LogicalVector3 Point,LogicalScalar Impulse,bool Lasting,DeterministicPhysicsWorld World,ref State State){
        if(Impulse<=LogicalScalar.Zero||!Instance.State.TryGetPiece(Piece,out var Owner)){ return; }
        if(!TryPose(Instance,Owner,Body,World,ref State,out var Position,out var Rotation)){ return; }
        var Local=Rotate(Rotation.Conjugate(),Point-Position)+(Owner.IsWhole?LogicalVector3.Zero:Owner.Center);
        Instance.State.AddImpulse(Instance.State.FragmentAt(Local,Owner),Impulse,Lasting);
    }
    /// <summary>A world-space strain sphere on one destructible: each intact bond is measured where its middle is now (on its piece's body).</summary>
    private void AddStrainSphere(Instance Instance,LogicalVector3 Center,LogicalScalar Radius,LogicalScalar Strain,DeterministicPhysicsWorld World,ref State State){
        var Asset=Instance.State.Asset;
        var Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
        for(var Bond=0;Bond<Asset.Bonds.Count;Bond++){
            if(Instance.State.IsBondBroken(Bond)){ continue; }
            var Pair=Asset.Bonds[Bond];
            if(Instance.State.PieceOf(Pair.FirstFragment) is not { }Piece){ continue; }
            var Body=Piece.IsWhole?Instance.Root:Instance.Bodies.GetValueOrDefault(Piece.Id);
            if(!TryPose(Instance,Piece,Body,World,ref State,out var Position,out var Rotation)){ continue; }
            var Middle=(Asset.Fragments[Pair.FirstFragment].LocalCenter+Asset.Fragments[Pair.SecondFragment].LocalCenter)*Half;
            var WorldMiddle=Position+Rotate(Rotation,Middle-(Piece.IsWhole?LogicalVector3.Zero:Piece.Center));
            var DistanceSquared=(WorldMiddle-Center).LengthSquared;
            if(DistanceSquared>Radius*Radius||!LogicalScalar.TrySquareRoot(DistanceSquared,out var Distance)){ continue; }
            Instance.State.AddStrain(Bond,Strain*(LogicalScalar.One-Distance/Radius));
        }
    }
    /// <summary>
    /// Turns the pieces that came apart into bodies: the whole destructible's own collider is switched off, or the split piece's body is destroyed, and each
    /// new piece gets a body where its fragments are now, moving as the old body moved at that point (anchored pieces are static). The drawn fragments move
    /// over to their new bodies.
    /// </summary>
    private void Split(Instance Instance,FractureStep Result,DeterministicPhysicsWorld World,ulong Tick,ref State State){
        Dictionary<int,(LogicalVector3 Position,LogicalQuaternion Rotation,PhysicsVelocity Velocity,LogicalVector3 Origin)>Old=[];
        foreach(var Piece in Result.Ended){
            var Body=Piece.IsWhole?Instance.Root:Instance.Bodies.GetValueOrDefault(Piece.Id);
            if(!TryPose(Instance,Piece,Body,World,ref State,out var Position,out var Rotation)){
                Position=LogicalVector3.Zero;
                Rotation=LogicalQuaternion.Identity;
            }
            var Velocity=World.TryGetBodyState(Body,out var Moving)&&Moving.MotionType==PhysicsMotionType.Dynamic?Moving.Velocity:PhysicsVelocity.Zero;
            Old[Piece.Id]=(Position,Rotation,Velocity,Piece.IsWhole?LogicalVector3.Zero:Piece.Center);
            if(Piece.IsWhole){
                State.World.SetComponentEnabled(Instance.Root,EcsComponentType.Collider,false);
                this.Owners.Remove(Instance.Root);
            }
            else {
                this.Owners.Remove(Body);
                Instance.Bodies.Remove(Piece.Id);
                Instance.Loose.RemoveAll(Entry=>Entry.Piece==Piece.Id);
                if(State.World.IsAlive(Body)){ State.World.DestroyDeferred(Body,ref State); }
            }
        }
        State.World.TryGetComponent(Instance.Root,out Transform RootTransform);
        // The fragment budget is kept before any body is made: the oldest loose pieces go first, and when the new ones alone exceed it, the first of them
        // are removed instead of being made (a body made in this tick could not be destroyed in it).
        var Excess=Instance.Settings.FragmentBudget>0?Instance.Loose.Count+Result.Began.Count(static Piece=>!Piece.Anchored)-Instance.Settings.FragmentBudget:0;
        while(Excess>0&&Instance.Loose.Count>0){
            this.RemovePiece(Instance,Instance.Loose[0].Piece,ref State);
            Excess--;
        }
        foreach(var Piece in Result.Began){
            if(!Piece.Anchored&&Excess>0){
                Excess--;
                this.RemovePiece(Instance,Piece.Id,ref State);
                continue;
            }
            var (OldPosition,Rotation,OldVelocity,Origin)=Old[Piece.Parent];
            var Position=OldPosition+Rotate(Rotation,Piece.Center-Origin);
            var Velocity=new PhysicsVelocity {
                Linear=OldVelocity.Linear+LogicalVector3.Cross(OldVelocity.Angular,Position-OldPosition),
                Angular=OldVelocity.Angular,
            };
            var Shape=Instance.State.ShapeOf(Piece);
            World.Shapes.Add(Shape);
            var Collider=Instance.Collider with {
                Shape=Shape.Kind==PhysicsShapeAssetKind.Compound?ColliderShape.Compound:ColliderShape.ConvexAsset,
                Asset=Shape.Id,
                Center=LogicalVector3.Zero,
                IsTrigger=false,
            };
            var Placement=new Transform { Frame=RootTransform.Frame,Position=Position,Rotation=Rotation,Scale=LogicalVector3.One };
            var StableId=new PhysicsStableId { Value=PieceStableId(Instance.RootStableId,Piece.Id) };
            var Tag=new DestructiblePiece { Root=Instance.Root,Piece=Piece.Id };
            Entity Body;
            if(Piece.Anchored){
                Body=State.World.CreateDeferred(Placement,Collider,new PhysicsRotation { Value=Rotation },StableId,Tag);
            }
            else {
                var Rigid=new RigidBody {
                    MotionType=PhysicsMotionType.Dynamic,Mass=Piece.Mass,AllowSleep=true,
                    GravityFactor=Instance.Dynamic?Instance.Body.GravityFactor:LogicalScalar.One,
                    LinearDamping=Instance.Dynamic?Instance.Body.LinearDamping:LogicalScalar.Zero,
                    AngularDamping=Instance.Dynamic?Instance.Body.AngularDamping:LogicalScalar.Zero,
                };
                Body=State.World.CreateDeferred(Placement,Collider,Rigid,new PhysicsRotation { Value=Rotation },Velocity,StableId,Tag);
                Instance.Loose.Add((Piece.Id,Tick));
            }
            Instance.Bodies[Piece.Id]=Body;
            this.Owners[Body]=(Instance,Piece.Id);
            foreach(var Fragment in Piece.Fragments){
                var Visual=Instance.Visuals[Fragment];
                if(!State.World.TryGetComponent(Visual,out DestructibleFragment Follow)){ continue; }
                Follow.Body=Body;
                Follow.Offset=Instance.State.Asset.Fragments[Fragment].LocalCenter-Piece.Center;
                State.World.TrySetComponent(Visual,in Follow);
            }
        }
    }
    /// <summary>Removes the loose pieces whose <see cref="Destructible.FragmentLifetime"/> ran out, then the oldest while more than <see cref="Destructible.FragmentBudget"/> live.</summary>
    private void Expire(Instance Instance,ulong Tick,ref State State){
        if(Instance.Settings.FragmentLifetime>LogicalScalar.Zero&&State.FixedTickRate>0){
            // The lifetime in whole ticks (rounded to the nearest, at least one).
            var LifetimeTicks=(Instance.Settings.FragmentLifetime*LogicalScalar.FromInt64(State.FixedTickRate)).TryToScaledInt64(0,out var Whole)?(ulong)Math.Max(1L,Whole):ulong.MaxValue/2;
            while(Instance.Loose.Count>0&&Tick>=Instance.Loose[0].Tick+LifetimeTicks){ this.RemovePiece(Instance,Instance.Loose[0].Piece,ref State); }
        }
        if(Instance.Settings.FragmentBudget>0){
            while(Instance.Loose.Count>Instance.Settings.FragmentBudget){ this.RemovePiece(Instance,Instance.Loose[0].Piece,ref State); }
        }
    }
    private void RemovePiece(Instance Instance,int PieceId,ref State State){
        Instance.Loose.RemoveAll(Entry=>Entry.Piece==PieceId);
        if(!Instance.State.TryGetPiece(PieceId,out var Piece)){ return; }
        foreach(var Fragment in Piece.Fragments){
            var Visual=Instance.Visuals[Fragment];
            if(State.World.IsAlive(Visual)){ State.World.DestroyDeferred(Visual,ref State); }
            Instance.Visuals[Fragment]=default;
        }
        if(Instance.Bodies.Remove(PieceId,out var Body)){
            this.Owners.Remove(Body);
            if(State.World.IsAlive(Body)){ State.World.DestroyDeferred(Body,ref State); }
        }
        Instance.State.Remove(PieceId);
    }
    /// <summary>A destructible whose Entity is gone (its scene was unloaded): its state goes, and <c>DestructionOrphans</c> removes its Entities.</summary>
    private void Unload(Instance Instance,ref State State){
        foreach(var Body in Instance.Bodies.Values){ this.Owners.Remove(Body); }
        this.Owners.Remove(Instance.Root);
        this.Instances.Remove(Instance.RootStableId);
    }
    /// <summary>
    /// Where a body is now and the frame of its piece: the physics world's pose when the body is in it (it was there for this tick's step), otherwise the
    /// Entity's own Transform (a body made this tick).
    /// </summary>
    private static bool TryPose(Instance Instance,FracturePiece Piece,Entity Body,DeterministicPhysicsWorld World,ref State State,out LogicalVector3 Position,out LogicalQuaternion Rotation){
        if(World.TryGetBodyState(Body,out var Physical)){
            Position=Physical.Position;
            Rotation=Physical.Rotation;
            return true;
        }
        if(State.World.TryGetComponent(Body,out Transform Transform)){
            Position=Transform.Position;
            Rotation=CurrentRotation(Body,State.World,Transform.Rotation);
            return true;
        }
        Position=default;
        Rotation=LogicalQuaternion.Identity;
        return false;
    }
    /// <summary>The rotation a body is drawn and simulated with: its <c>PhysicsRotation</c> when it has one, otherwise its Transform's.</summary>
    internal static LogicalQuaternion CurrentRotation(Entity Entity,CSharpGameEngine.Entities.World World,LogicalQuaternion Fallback)=>
        World.TryGetComponent(Entity,out PhysicsRotation Rotation)?Rotation.Value:Fallback;
    internal static LogicalVector3 Rotate(LogicalQuaternion Rotation,LogicalVector3 Value)=>
        Rotation.TryRotate(Value,out var Result)?Result:throw new OverflowException("A destructible's pose does not fit the logical range.");
    private void Fail(ref DestructibleState Runtime,string Problem){
        Runtime.Problem=Problem;
        Console.Error.WriteLine("CGE-DESTRUCTION "+Problem);
    }
}
