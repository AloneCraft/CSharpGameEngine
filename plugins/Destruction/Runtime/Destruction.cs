using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Destruction;
/// <summary>
/// ゲーム内の破壊可能オブジェクト (ADR-0078/M0113)。System の引数に <c>Destruction</c> と書くと、同じゲームのすべての System が同じインスタンスを受け取る。各破壊可能オブジェクトの破断状態（<see cref="FractureState"/>。<c>Runtime.Physics</c> のソルバー）と、それに対して生成した Entity をすべて所有する。調理済みの各フラグメントに描画用フラグメントを 1 つ作り、オブジェクトが分離した後は破片ごとに剛体を 1 つ作る。ゲームから破壊可能オブジェクトにひずみを加えたり（<see cref="ApplyStrain"/>：射撃、爆発）、力積を与えたり（<see cref="ApplyImpulse"/>）できる。ひずみは物理ステップの後、同じ固定 tick 内で解決する。
/// すべての処理を破壊可能オブジェクトの物理安定 ID 順に行うため、毎回同じ実行結果になる。
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
        /// <summary>自由になった順に並べた破片と、それぞれが分離したティック。</summary>
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
    /// <summary>各キャラクターが各ボディに触れた最後のティック（M0124）：キャラクターの衝撃が有効なのは、接触を始めたティックだけ。接触の場合と同じ。</summary>
    private readonly Dictionary<(Entity Character,Entity Body),ulong>CharacterTouches=[];
    private readonly List<(Entity Character,Entity Body)>StaleTouches=[];
    private readonly List<(Entity Body,LogicalVector3 Point,LogicalScalar Impulse,bool Lasting)>PendingHits=[];
    private readonly List<ulong>Ended=[];
    private uint Sequence;
    /// <summary>読み込まれている破壊可能オブジェクトの数。</summary>
    public int Count=>this.Instances.Count;
    /// <summary>読み込み済み破壊可能オブジェクトの破壊状態（ゲームとテスト用）。</summary>
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
    /// <summary>破壊可能オブジェクトの破片を描画する Entity。削除後は <see cref="Entity"/> の既定値。</summary>
    public Entity FragmentEntity(Entity Root,int Fragment){
        foreach(var Instance in this.Instances.Values){
            if(Instance.Root==Root&&(uint)Fragment<(uint)Instance.Visuals.Length){ return Instance.Visuals[Fragment]; }
        }
        return default;
    }
    /// <summary>生存中の破片のボディ（オブジェクトが全体の間は、破壊可能オブジェクト自体）。</summary>
    public Entity PieceBody(Entity Root,int Piece){
        foreach(var Instance in this.Instances.Values){
            if(Instance.Root!=Root){ continue; }
            if(Piece==0&&Instance.State.IsWhole){ return Root; }
            return Instance.Bodies.TryGetValue(Piece,out var Body)?Body:default;
        }
        return default;
    }
    /// <summary>Entity が、全体状態の破壊可能オブジェクトか、その破片のボディか。</summary>
    public bool Owns(Entity Body)=>this.Owners.ContainsKey(Body);
    /// <summary>
    /// ワールド座標 <paramref name="Center"/> から <paramref name="Radius"/> 以内に中心があるすべての結合に、距離に応じて弱まり、半径上でゼロになる
    /// <paramref name="Strain"/>（N·s）を加える（UE の External Strain field）。この固定ティックの破壊処理で解決する。
    /// </summary>
    public void ApplyStrain(LogicalVector3 Center,LogicalScalar Radius,LogicalScalar Strain){
        if(Radius>LogicalScalar.Zero&&Strain>LogicalScalar.Zero){ this.PendingStrain.Add((Center,Radius,Strain)); }
    }
    /// <summary>破壊可能オブジェクト（または破片のボディ）をワールド座標上の点で力積（N·s）により叩く。その位置の破片と隣接破片にひずみを加える。衝撃なので、結合の内部ひずみにも加算する（M0124）。</summary>
    public void ApplyImpulse(Entity Body,LogicalVector3 Point,LogicalScalar Impulse){
        if(Impulse>LogicalScalar.Zero&&this.Owners.ContainsKey(Body)){ this.PendingImpulses.Add((Body,Point,Impulse)); }
    }
    /// <summary>
    /// キャラクターによる衝撃（<see cref="DestructionImpact"/>、M0124）：衝撃が発生するのは、キャラクターがそのボディに触れ始めたティックだけ。押し続けても、そのティック分
    /// のひずみだけを加える。継続する接触と同じ扱いで、箱にもたれ続けても毎ティック摩耗させない。
    /// </summary>
    internal void ApplyCharacterImpulse(Entity Character,Entity Body,LogicalVector3 Point,LogicalScalar Impulse,ulong Tick){
        if(Impulse<=LogicalScalar.Zero||!this.Owners.ContainsKey(Body)){ return; }
        var Began=!(this.CharacterTouches.TryGetValue((Character,Body),out var Last)&&Last+1>=Tick);
        this.CharacterTouches[(Character,Body)]=Tick;
        this.PendingHits.Add((Body,Point,Impulse,Began));
    }
    /// <summary>メモリ上で作成したアセットを、破壊可能オブジェクトが <paramref name="Name"/> で検索できる場所に登録する（テストとプレビュー用。ビルドでは実アセットを調理する）。</summary>
    public static void Register(string Name,PrefractureAsset Asset)=>PrefractureAssetCache.Register(ContentRoot.Resolve(DestructionDefaults.AssetPath(Name)),Asset);
    /// <summary>破片のボディの安定 ID。破壊可能オブジェクトの ID と破片番号から計算する FNV-1a ハッシュで、最上位ビットを立て、Entity 番号の ID と重ならないようにする。</summary>
    public static ulong PieceStableId(ulong RootStableId,int Piece){
        var Hash=14695981039346656037UL;
        Hash=(Hash^RootStableId)*1099511628211UL;
        Hash=(Hash^0xf7ac7UL)*1099511628211UL;
        Hash=(Hash^(ulong)(uint)Piece)*1099511628211UL;
        return Hash|(1UL<<63);
    }
    /// <summary>初めて見つかった破壊可能オブジェクトを読み込む。事前生成したアセット、その破壊状態、および破片ごとに描画用 Entity を1つ作成し、破壊されるまではオブジェクトに追従させる。</summary>
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
    /// 物理ステップ後に実行する、固定ティック1回分の破壊処理。通常実行では、そのティックのひずみ（破壊可能オブジェクトとその破片への接触力積、ゲームからの力積、ひずみフィールド）を集め、内部ひずみ（
    /// M0124）を含むひずみが強度に達した結合を破壊する。それぞれを <c>BondBroken</c> イベントとキャッシュ記録として報告し、衝撃で内部ひずみが増えた結合も記録する（キャッシュはその値を
    /// 保持する）。分離した部分はボディにする。衝撃として残るのは、接触開始ティックの接触、ゲームからの力積（キャラクターの衝撃もこれに含む）、フィールドである。物体の上に載り続けるボディによる継続接触は、そ
    /// のティック分だけひずみを加える。キャッシュ再生時は、記録された結合をそのティックに破壊し、記録済みの内部ひずみを復元する（ひずみ入力は無視する）。これにより同じ破片が現れ、その後の通常実行も記録どおり
    /// に続く。最後に、寿命が尽きた破片、または破片数の上限を超えた破片を削除する。
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
        // このティックに更新されなかった接触は終了している。
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
    /// <summary>フィールドの範囲内で動く各ボディを一度だけ外向きに押す（<see cref="ForceField"/>）。力積はボディの質量とフィールド速度の積で、距離に応じて弱まり、次のティックに適用される。</summary>
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
    /// <summary>削除されたフィールドが押したボディの記録を破棄する。</summary>
    internal void ForgetField(Entity Field)=>this.Pushed.Remove(Field);
    private static ulong FieldSource(Entity Field)=>(1UL<<62)|(ulong)(uint)Field.Index;
    /// <summary>ワールド座標上のボディ（破壊可能オブジェクト自体、または破片）への点力積を、その点を含む破片に適用する。<paramref name="Lasting"/> の場合は衝撃として内部ひずみにも加える。</summary>
    private void AddImpulse(Instance Instance,int Piece,Entity Body,LogicalVector3 Point,LogicalScalar Impulse,bool Lasting,DeterministicPhysicsWorld World,ref State State){
        if(Impulse<=LogicalScalar.Zero||!Instance.State.TryGetPiece(Piece,out var Owner)){ return; }
        if(!TryPose(Instance,Owner,Body,World,ref State,out var Position,out var Rotation)){ return; }
        var Local=Rotate(Rotation.Conjugate(),Point-Position)+(Owner.IsWhole?LogicalVector3.Zero:Owner.Center);
        Instance.State.AddImpulse(Instance.State.FragmentAt(Local,Owner),Impulse,Lasting);
    }
    /// <summary>単一の破壊可能オブジェクトに作用するワールド空間のひずみ球。各未破壊結合の現在位置（所属する破片のボディ上）で距離を測る。</summary>
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
    /// 分離した破片をボディにする。オブジェクト全体なら自身のコライダーを無効にし、分割済み破片ならそのボディを破棄する。新しい各破片には現在位置にボディを作り、元のボディのその位置での動きを引き継がせる（
    /// 固定破片は静止）。描画用破片も新しいボディへ移す。
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
        // 破片数の上限はボディを作る前に適用する。まず古い分離破片を削除し、新たに生じた破片だけで上限を超える場合は、その先頭の破片を
        // 削除する（このティックに作成したボディは同じティック内には破棄できない）。
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
    /// <summary>
    /// <see cref="Destructible.FragmentLifetime"/> が尽きた分離破片を削除し、生存中の破片が
    /// <see cref="Destructible.FragmentBudget"/> を超えていれば古いものから削除する。
    /// </summary>
    private void Expire(Instance Instance,ulong Tick,ref State State){
        if(Instance.Settings.FragmentLifetime>LogicalScalar.Zero&&State.FixedTickRate>0){
            // 寿命を整数ティック数に換算する（最も近い値に丸め、最低1）。
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
    /// <summary>Entity が消えた破壊可能オブジェクト（シーンがアンロードされた状態）。状態を破棄し、<c>DestructionOrphans</c> が関連 Entity を削除する。</summary>
    private void Unload(Instance Instance,ref State State){
        foreach(var Body in Instance.Bodies.Values){ this.Owners.Remove(Body); }
        this.Owners.Remove(Instance.Root);
        this.Instances.Remove(Instance.RootStableId);
    }
    /// <summary>ボディの現在位置と、その破片の座標枠。物理ワールド内のボディなら物理ワールドの姿勢（このティックのステップで使われたもの）、それ以外なら Entity 自身の Transform（このティックに作成されたボディ）。</summary>
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
    /// <summary>ボディの描画とシミュレーションに使う回転。<c>PhysicsRotation</c> があればその値、なければ Transform の回転。</summary>
    internal static LogicalQuaternion CurrentRotation(Entity Entity,CSharpGameEngine.Entities.World World,LogicalQuaternion Fallback)=>
        World.TryGetComponent(Entity,out PhysicsRotation Rotation)?Rotation.Value:Fallback;
    internal static LogicalVector3 Rotate(LogicalQuaternion Rotation,LogicalVector3 Value)=>
        Rotation.TryRotate(Value,out var Result)?Result:throw new OverflowException("A destructible's pose does not fit the logical range.");
    private void Fail(ref DestructibleState Runtime,string Problem){
        Runtime.Problem=Problem;
        Console.Error.WriteLine("CGE-DESTRUCTION "+Problem);
    }
}
