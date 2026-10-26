using CSharpGameEngine.Entities;
using SimulationCommand=CSharpGameEngine.Runtime.Simulation.SimulationCommand<CSharpGameEngine.Runtime.Physics.PhysicsCommand>;

namespace CSharpGameEngine.Runtime.Physics;

/// <summary>Deterministic single-writer sequential rigid-body world for a local meter domain.</summary>
public sealed class DeterministicPhysicsWorld:IReadOnlyPhysicsWorld {
    private sealed class Body(in PhysicsBodyDescription Description,PhysicsShapeAsset? Asset) {
        internal readonly Entity Entity=Description.Entity;
        internal readonly ulong StableId=Description.StableId;
        internal Collider Collider=Description.Collider;
        internal PhysicsShapeAsset? Asset=Asset;
        internal RigidBody? RigidBody=Description.RigidBody;
        internal LogicalVector3 Position=Description.Position;
        internal LogicalQuaternion Rotation=Description.Rotation;
        internal PhysicsVelocity Velocity=Description.Velocity;
        internal LogicalVector3 InitialPosition=Description.Position;
        internal LogicalQuaternion InitialRotation=Description.Rotation;
        internal PhysicsVelocity InitialVelocity=Description.Velocity;
        internal bool IsSleeping;
        internal ushort SleepTicks;
        internal bool Faulted;
        /// <summary>ADR-0078/M0113: a body it touched was removed or changed, so a sleeping body wakes at the start of the next step (it may have lost its support).</summary>
        internal bool WakeRequested;
    }

    private struct CachedPoint {
        /// <summary>The normal of the point's patch (M0124: one contact holds a patch per face orientation), from the first body into the second.</summary>
        internal LogicalVector3 Normal;
        internal LogicalVector3 Position;
        internal LogicalScalar Penetration;
        internal LogicalScalar NormalImpulse;
        internal LogicalScalar TangentImpulse;
        internal LogicalVector3 Tangent;
        /// <summary>The speed this point leaves the surface at, from the approach speed before the solve (every iteration aims at the same value).</summary>
        internal LogicalScalar Bounce;
        /// <summary>A speculative point (negative penetration) that reaches the surface in this step, so it is held like a touching one.</summary>
        internal bool Reaching;
        /// <summary>The approach speed of the last step was an impact (above the bounce threshold), so its impulse is not carried into the next step as a support force.</summary>
        internal bool Impacted;
    }

    private sealed class CachedContact(Body First,Body Second) {
        internal readonly Body First=First;
        internal readonly Body Second=Second;
        /// <summary>The points of every patch, packed in patch order (patch k is <see cref="PatchStart"/>[k] .. + <see cref="PatchCount"/>[k]).</summary>
        internal readonly CachedPoint[] Points=new CachedPoint[PhysicsNarrowPhase.MaximumContactPoints];
        /// <summary>The points of the last tick while the new patches take over their warm-started impulses.</summary>
        internal readonly CachedPoint[] Previous=new CachedPoint[PhysicsNarrowPhase.MaximumContactPoints];
        internal int PointCount;
        internal int Patches;
        internal readonly int[] PatchStart=new int[PhysicsNarrowPhase.MaximumContactPatches];
        internal readonly int[] PatchCount=new int[PhysicsNarrowPhase.MaximumContactPatches];
        internal readonly uint[] PatchFeature=new uint[PhysicsNarrowPhase.MaximumContactPatches];
        internal readonly LogicalVector3[] PatchNormal=new LogicalVector3[PhysicsNarrowPhase.MaximumContactPatches];
        internal bool Active;
        internal bool WasActive;
        internal bool IsTrigger;
        /// <summary>Solved this tick: one of its bodies is a dynamic body that is awake (a contact between sleeping or static bodies only holds).</summary>
        internal bool Solve;
        internal LogicalScalar MaximumPenetration {
            get {
                LogicalScalar Result=LogicalScalar.Zero;
                for(int Index=0;Index<this.PointCount;Index++){
                    if(this.Points[Index].Penetration>Result){ Result=this.Points[Index].Penetration; }
                }
                return Result;
            }
        }
        internal LogicalScalar TotalNormalImpulse {
            get {
                LogicalScalar Result=LogicalScalar.Zero;
                for(int Index=0;Index<this.PointCount;Index++){ Result+=this.Points[Index].NormalImpulse; }
                return Result;
            }
        }
    }

    private sealed class PairKeyComparer:IComparer<(ulong First,ulong Second)> {
        internal static readonly PairKeyComparer Instance=new();
        public int Compare((ulong First,ulong Second) Left,(ulong First,ulong Second) Right){
            int Result=Left.First.CompareTo(Right.First);
            return Result!=0?Result:Left.Second.CompareTo(Right.Second);
        }
    }

    private readonly record struct CcdContact(ulong First,ulong Second,PhysicsContactCandidate Contact);

    private static readonly LogicalScalar FixedDelta=LogicalScalar.FromSingle(1f/60f);
    private static readonly LogicalVector3 Gravity=new(0,LogicalScalar.FromSingle(-9.80665f),0);
    private static readonly LogicalScalar SleepLinearSquared=LogicalScalar.FromSingle(0.0001f);
    private static readonly LogicalScalar RestitutionVelocityThreshold=LogicalScalar.FromSingle(0.5f);
    private static readonly LogicalScalar PositionCorrection=LogicalScalar.FromSingle(0.8f);
    private static readonly LogicalScalar ContactSlop=LogicalScalar.FromSingle(0.0001f);
    private readonly SortedList<ulong,Body> Bodies=[];
    private readonly Dictionary<Entity,ulong> StableIdByEntity=[];
    private readonly DeterministicAabbTree Broadphase=new();
    private readonly PhysicsNarrowPhase NarrowPhase=new();
    private readonly List<PhysicsContactCandidate> ContactScratch=[];
    private readonly List<PhysicsContactCandidate> PatchScratch=[];
    private readonly SortedList<(ulong First,ulong Second),CachedContact> ContactCache=new(PairKeyComparer.Instance);
    private readonly List<ulong> QueryCandidates=[];
    private readonly List<SimulationCommand> CommittedCommands=[];
    private readonly List<PhysicsRayHit> SweepRayHits=[];
    private readonly List<PhysicsEvent> MutableEvents=[];
    private readonly List<(ulong First,ulong Second)> ContactRemovalScratch=[];
    private readonly List<CcdContact> CcdContacts=[];
    private readonly HashSet<ulong> CacheValidationIds=[];

    private readonly List<PhysicsBondBreak> MutableBondBreaks=[];
    private readonly List<PhysicsBondStrain> MutableBondStrains=[];

    public int BodyCount=>this.Bodies.Count;
    public IReadOnlyList<PhysicsEvent> Events=>this.MutableEvents;
    /// <summary>
    /// The bonds of destructibles that broke in the last tick (ADR-0078/M0113): reported by the destruction solver after <see cref="Step"/>, or restored from a
    /// played-back cache frame. Cleared when the next tick starts.
    /// </summary>
    public IReadOnlyList<PhysicsBondBreak> BondBreaks=>this.MutableBondBreaks;
    /// <summary>
    /// The bonds of destructibles whose internal strain an impact raised in the last tick, with the internal strain each carries on (M0124): reported by the
    /// destruction solver after <see cref="Step"/>, or restored from a played-back cache frame. Cleared when the next tick starts.
    /// </summary>
    public IReadOnlyList<PhysicsBondStrain> BondStrains=>this.MutableBondStrains;
    /// <summary>Records that an impact raised a destructible bond's internal strain in this tick (M0124); the cache keeps it. No event is raised.</summary>
    public void ReportBondStrain(ulong RootStableId,int Bond,LogicalScalar Strain){
        if(RootStableId==0||Bond<0||Strain<=LogicalScalar.Zero){ throw new ArgumentOutOfRangeException(nameof(Bond),"An internal strain names a destructible's stable ID, a bond and a strain above zero."); }
        this.MutableBondStrains.Add(new PhysicsBondStrain(RootStableId,Bond,Strain));
    }

    /// <summary>
    /// Records that a destructible's bond broke in <paramref name="Tick"/> (ADR-0078/M0113): it joins <see cref="BondBreaks"/> (the cache keeps it) and
    /// <see cref="Events"/> as a <see cref="PhysicsEventType.BondBroken"/> event whose <see cref="PhysicsEvent.First"/> is the destructible, whose impulse is
    /// the strain that broke it and whose pair ID is unique to the bond.
    /// </summary>
    public void ReportBondBreak(ulong Tick,Entity Destructible,ulong RootStableId,int Bond,LogicalScalar Strain){
        if(RootStableId==0||Bond<0||Strain<LogicalScalar.Zero){ throw new ArgumentOutOfRangeException(nameof(Bond),"A bond break names a destructible's stable ID, a bond and a strain of zero or more."); }
        this.MutableBondBreaks.Add(new PhysicsBondBreak(RootStableId,Bond,Strain));
        // Kept in the order Step leaves the events in (tick, pair ID, type), so readers see one canonical order.
        var Event=new PhysicsEvent(Tick,BondPairId(RootStableId,Bond),PhysicsEventType.BondBroken,Destructible,default,Strain,default);
        var Index=this.MutableEvents.Count;
        while(Index>0&&CompareEvents(this.MutableEvents[Index-1],Event)>0){ Index--; }
        this.MutableEvents.Insert(Index,Event);
    }

    private static int CompareEvents(in PhysicsEvent Left,in PhysicsEvent Right){
        int Result=Left.Tick.CompareTo(Right.Tick);
        if(Result!=0){ return Result; }
        Result=Left.PairId.CompareTo(Right.PairId);
        return Result!=0?Result:Left.Type.CompareTo(Right.Type);
    }

    /// <summary>The pair ID of a <see cref="PhysicsEventType.BondBroken"/> event: an FNV-1a hash of the destructible's stable ID and the bond, so each bond's event is ordered on its own.</summary>
    public static ulong BondPairId(ulong RootStableId,int Bond){
        ulong Hash=14695981039346656037UL;
        Hash=(Hash^RootStableId)*1099511628211UL;
        Hash=(Hash^0xb0dUL)*1099511628211UL;
        return (Hash^(ulong)(uint)Bond)*1099511628211UL;
    }
    public PhysicsCommands Commands { get; }=new();
    public PhysicsShapeCatalog Shapes { get; }=new();
    public LogicalVector3 DomainOrigin { get;set; }=LogicalVector3.Zero;

    public PhysicsUpdateStatus Upsert(in PhysicsBodyDescription Description){
        PhysicsUpdateStatus Status=this.ValidateAndCalculateBounds(Description,out PhysicsAabb Bounds,out PhysicsShapeAsset? Asset);
        if(Status!=PhysicsUpdateStatus.Success){ return Status; }
        if(this.StableIdByEntity.TryGetValue(Description.Entity,out ulong OldStableId)&&OldStableId!=Description.StableId){
            this.Remove(Description.Entity);
        }
        if(this.Bodies.TryGetValue(Description.StableId,out Body? Body)){
            if(Body.Entity!=Description.Entity){ return PhysicsUpdateStatus.InvalidInput; }
            Body.Collider=Description.Collider;
            Body.Asset=Asset;
            Body.RigidBody=Description.RigidBody;
            Body.Position=Description.Position;
            Body.Rotation=Description.Rotation;
            Body.Velocity=Description.Velocity;
            Body.InitialPosition=Description.Position;
            Body.InitialRotation=Description.Rotation;
            Body.InitialVelocity=Description.Velocity;
            Body.Faulted=false;
        }
        else {
            Body=new Body(Description,Asset);
            this.Bodies.Add(Description.StableId,Body);
        }
        this.StableIdByEntity[Description.Entity]=Description.StableId;
        this.RemoveContactsFor(Description.StableId);
        this.Broadphase.Upsert(Description.StableId,Bounds);
        return PhysicsUpdateStatus.Success;
    }

    internal PhysicsUpdateStatus Synchronize(in PhysicsBodyDescription Description){
        PhysicsUpdateStatus Status=this.ValidateAndCalculateBounds(Description,out PhysicsAabb Bounds,out PhysicsShapeAsset? Asset);
        if(Status!=PhysicsUpdateStatus.Success){ return Status; }
        if(this.StableIdByEntity.TryGetValue(Description.Entity,out ulong OldStableId)&&OldStableId!=Description.StableId){
            this.Remove(Description.Entity);
        }
        if(!this.Bodies.TryGetValue(Description.StableId,out Body? Body)){
            return this.Upsert(Description);
        }
        if(Body.Entity!=Description.Entity){ return PhysicsUpdateStatus.InvalidInput; }
        PhysicsMotionType? PreviousMotionType=Body.RigidBody?.MotionType;
        PhysicsMotionType? CurrentMotionType=Description.RigidBody?.MotionType;
        bool ShapeChanged=!EqualityComparer<Collider>.Default.Equals(Body.Collider,Description.Collider)
            ||!ReferenceEquals(Body.Asset,Asset);
        Body.Collider=Description.Collider;
        Body.Asset=Asset;
        Body.RigidBody=Description.RigidBody;
        if(CurrentMotionType is null||CurrentMotionType!=PreviousMotionType){
            Body.Position=Description.Position;
            Body.Rotation=Description.Rotation;
            Body.Velocity=Description.Velocity;
            Body.IsSleeping=false;
            Body.SleepTicks=0;
        }
        Body.Faulted=false;
        if(ShapeChanged){ this.RemoveContactsFor(Description.StableId); }
        this.StableIdByEntity[Description.Entity]=Description.StableId;
        this.Broadphase.Upsert(Description.StableId,Bounds);
        return PhysicsUpdateStatus.Success;
    }

    public bool Remove(Entity Entity){
        if(!this.StableIdByEntity.Remove(Entity,out ulong StableId)){ return false; }
        this.Broadphase.Remove(StableId);
        this.Bodies.Remove(StableId);
        this.RemoveContactsFor(StableId);
        return true;
    }

    public void Clear(){
        this.Bodies.Clear();
        this.StableIdByEntity.Clear();
        this.Broadphase.Clear();
        this.ContactCache.Clear();
        this.CcdContacts.Clear();
        this.MutableEvents.Clear();
        this.MutableBondBreaks.Clear();
        this.MutableBondStrains.Clear();
    }

    internal void Reset(ulong Tick){
        this.MutableEvents.Clear();
        this.MutableBondBreaks.Clear();
        this.MutableBondStrains.Clear();
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            Body.Position=Body.InitialPosition;
            Body.Rotation=Body.InitialRotation;
            Body.Velocity=Body.InitialVelocity;
            Body.IsSleeping=false;
            Body.SleepTicks=0;
            Body.Faulted=false;
            Body.WakeRequested=false;
            this.Broadphase.Upsert(Body.StableId,this.CalculateBounds(Body));
        }
        this.ContactCache.Clear();
        this.CcdContacts.Clear();
    }
    public void Step(ulong Tick){
        this.MutableEvents.Clear();
        this.MutableBondBreaks.Clear();
        this.MutableBondStrains.Clear();
        this.CcdContacts.Clear();
        for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
            CachedContact Contact=this.ContactCache.Values[ContactIndex];
            Contact.WasActive=Contact.Active;
            Contact.Active=false;
        }
        this.ApplyCommands(Tick);
        for(int BodyIndex=0;BodyIndex<this.Bodies.Count;BodyIndex++){
            Body Body=this.Bodies.Values[BodyIndex];
            if(!Body.WakeRequested){ continue; }
            Body.WakeRequested=false;
            if(IsDynamic(Body)&&Body.IsSleeping){ Wake(Body,Tick,this.MutableEvents); }
        }
        for(int BodyIndex=0;BodyIndex<this.Bodies.Count;BodyIndex++){
            Body Body=this.Bodies.Values[BodyIndex];
            if(Body.Faulted||Body.IsSleeping||Body.RigidBody is not { MotionType:PhysicsMotionType.Dynamic } RigidBody){ continue; }
            LogicalVector3 PreviousPosition=Body.Position;
            LogicalQuaternion PreviousRotation=Body.Rotation;
            PhysicsVelocity PreviousVelocity=Body.Velocity;
            try {
                LogicalVector3 Acceleration=Gravity*RigidBody.GravityFactor;
                Body.Velocity.Linear+=Acceleration*FixedDelta;
                // ADR-0078/M0109: UE's (Chaos's) linear and angular damping, v × max(0, 1 - damping × Δt). Zero damping leaves the velocity exact.
                if(!RigidBody.LinearDamping.IsZero){ Body.Velocity.Linear*=DampingFactor(RigidBody.LinearDamping); }
                if(!RigidBody.AngularDamping.IsZero){ Body.Velocity.Angular*=DampingFactor(RigidBody.AngularDamping); }
                Body.Position=this.IntegratePositionWithCcd(Body,PreviousPosition,Body.Position+Body.Velocity.Linear*FixedDelta);
                Body.Rotation=IntegrateRotation(Body.Rotation,Body.Velocity.Angular);
                this.Broadphase.Upsert(Body.StableId,this.CalculateBounds(Body));
            }
            catch(OverflowException){
                Body.Position=PreviousPosition;
                Body.Rotation=PreviousRotation;
                Body.Velocity=PreviousVelocity;
                this.FaultBody(Body,Tick,PhysicsFaultType.NumericOverflow);
            }
        }
        this.BuildContacts();
        this.SolveContacts(Tick);
        this.PublishContactEvents(Tick);
        for(int BodyIndex=0;BodyIndex<this.Bodies.Count;BodyIndex++){
            Body Body=this.Bodies.Values[BodyIndex];
            if(!Body.Faulted&&Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }){ this.UpdateSleep(Body,Tick); }
            this.Broadphase.Upsert(Body.StableId,this.CalculateBounds(Body));
        }
        if(this.MutableEvents.Count>1){
        if(this.MutableEvents.Count>1){
            this.MutableEvents.Sort(static(Left,Right)=>{
                int Result=Left.Tick.CompareTo(Right.Tick);
                if(Result!=0){ return Result; }
                Result=Left.PairId.CompareTo(Right.PairId);
                return Result!=0?Result:Left.Type.CompareTo(Right.Type);
            });
        }
        }
    }

    public PhysicsQueryResult Raycast(in PhysicsRay Ray,PhysicsLayerMask Layers,List<PhysicsRayHit> Results){
        ArgumentNullException.ThrowIfNull(Results);
        if(Ray.MaxDistance<LogicalScalar.Zero||Ray.Direction.LengthSquared.IsZero){
            return new PhysicsQueryResult(PhysicsQueryStatus.InvalidInput,0);
        }
        int Start=Results.Count;
        try {
            LogicalVector3 End=Ray.Origin+Ray.Direction*Ray.MaxDistance;
            PhysicsAabb RayBounds=new(
                new LogicalVector3(Min(Ray.Origin.R,End.R),Min(Ray.Origin.U,End.U),Min(Ray.Origin.F,End.F)),
                new LogicalVector3(Max(Ray.Origin.R,End.R),Max(Ray.Origin.U,End.U),Max(Ray.Origin.F,End.F)));
            this.QueryCandidates.Clear();
            this.Broadphase.Query(RayBounds,this.QueryCandidates);
            foreach(ulong StableId in this.QueryCandidates){
                Body Body=this.Bodies[StableId];
                if(!Layers.Contains(Body.Collider.Layer)){ continue; }
                if(Body.Collider.Shape==ColliderShape.StaticTriangleMesh){
                    // 三角形メッシュは外接ボックスではなく、三角形そのものに当てる(坂や穴のある形でも正確)。
                    if(!Body.Faulted&&this.NarrowPhase.TryRaycastMesh(Ray,this.CreateShape(Body),out LogicalScalar MeshDistance,out LogicalVector3 MeshNormal)){
                        Results.Add(new PhysicsRayHit(Body.Entity,StableId,MeshDistance,Ray.Origin+Ray.Direction*MeshDistance,MeshNormal));
                    }
                    continue;
                }
                if(TryRayAabb(Ray,CalculateBounds(Body),out LogicalScalar Distance,out LogicalVector3 Normal)){
                    Results.Add(new PhysicsRayHit(Body.Entity,StableId,Distance,Ray.Origin+Ray.Direction*Distance,Normal));
                }
            }
            Results.Sort(Start,Results.Count-Start,PhysicsRayHitComparer.Instance);
            return new PhysicsQueryResult(PhysicsQueryStatus.Success,Results.Count-Start);
        }
        catch(OverflowException){
            Results.RemoveRange(Start,Results.Count-Start);
            return new PhysicsQueryResult(PhysicsQueryStatus.NumericOverflow,0);
        }
    }

    public PhysicsQueryResult Sweep(in Collider Shape,in LogicalVector3 Origin,in LogicalVector3 Direction,LogicalScalar Distance,PhysicsLayerMask Layers,List<PhysicsSweepHit> Results){
        ArgumentNullException.ThrowIfNull(Results);
        if(Distance<LogicalScalar.Zero||Direction.LengthSquared.IsZero||Shape.Shape==ColliderShape.StaticTriangleMesh){
            return new PhysicsQueryResult(PhysicsQueryStatus.InvalidInput,0);
        }
        int Start=Results.Count;
        LogicalVector3 HalfExtents=ShapeHalfExtents(Shape);
        PhysicsRay Ray=new(Origin,Direction,Distance);
        this.SweepRayHits.Clear();
        PhysicsQueryResult RayResult=this.Raycast(Ray,Layers,this.SweepRayHits);
        if(RayResult.Status!=PhysicsQueryStatus.Success){ return new PhysicsQueryResult(RayResult.Status,0); }
        foreach(PhysicsRayHit Hit in this.SweepRayHits){
            LogicalScalar ExpandedDistance=Max(LogicalScalar.Zero,Hit.Distance-Max(HalfExtents.R,Max(HalfExtents.U,HalfExtents.F)));
            Results.Add(new PhysicsSweepHit(Hit.Entity,Hit.StableId,ExpandedDistance,Ray.Origin+Ray.Direction*ExpandedDistance,Hit.Normal));
        }
        return new PhysicsQueryResult(PhysicsQueryStatus.Success,Results.Count-Start);
    }

    /// <summary>
    /// Every penetration of a sphere or capsule placed at <paramref name="Position"/>: one contact per
    /// penetrating triangle of a static triangle mesh and one per other body, ordered by stable ID
    /// then feature. Normals point from the query shape into the body. Triggers are skipped.
    /// </summary>
    public PhysicsQueryResult CollectContacts(in Collider Shape,in LogicalVector3 Position,in LogicalQuaternion Rotation,PhysicsLayerMask Layers,List<PhysicsShapeContact> Results){
        ArgumentNullException.ThrowIfNull(Results);
        if(Shape.Shape is not (ColliderShape.Sphere or ColliderShape.Capsule)||Shape.Radius<=LogicalScalar.Zero||Shape.HalfHeight<LogicalScalar.Zero){
            return new PhysicsQueryResult(PhysicsQueryStatus.InvalidInput,0);
        }
        int Start=Results.Count;
        try {
            PhysicsCollisionShape Query=new(Shape,Position,Rotation,null);
            PhysicsAabb Bounds=this.NarrowPhase.CalculateBounds(Query);
            this.QueryCandidates.Clear();
            this.Broadphase.Query(Bounds,this.QueryCandidates);
            this.QueryCandidates.Sort();
            foreach(ulong StableId in this.QueryCandidates){
                Body Body=this.Bodies[StableId];
                if(Body.Faulted||!Layers.Contains(Body.Collider.Layer)||Body.Collider.IsTrigger){ continue; }
                PhysicsCollisionShape Target=this.CreateShape(Body);
                if(Body.Collider.Shape==ColliderShape.StaticTriangleMesh){
                    this.ContactScratch.Clear();
                    this.NarrowPhase.CollectMeshContacts(Query,Target,this.ContactScratch);
                    foreach(PhysicsContactCandidate Contact in this.ContactScratch){
                        Results.Add(new PhysicsShapeContact(Body.Entity,StableId,Contact.Position,Contact.Normal,Contact.Penetration,
                            Contact.SurfaceNormal.LengthSquared.IsZero?-Contact.Normal:Contact.SurfaceNormal));
                    }
                }
                else if(this.NarrowPhase.TryCreateContact(Query,Target,out PhysicsContactCandidate Contact)){
                    Results.Add(new PhysicsShapeContact(Body.Entity,StableId,Contact.Position,Contact.Normal,Contact.Penetration,-Contact.Normal));
                }
            }
            return new PhysicsQueryResult(PhysicsQueryStatus.Success,Results.Count-Start);
        }
        catch(OverflowException){
            Results.RemoveRange(Start,Results.Count-Start);
            return new PhysicsQueryResult(PhysicsQueryStatus.NumericOverflow,0);
        }
    }

    public PhysicsQueryResult Overlap(in PhysicsAabb Bounds,PhysicsLayerMask Layers,List<PhysicsOverlapHit> Results){
        ArgumentNullException.ThrowIfNull(Results);
        int Start=Results.Count;
        this.QueryCandidates.Clear();
        this.Broadphase.Query(Bounds,this.QueryCandidates);
        foreach(ulong StableId in this.QueryCandidates){
            Body Body=this.Bodies[StableId];
            if(Layers.Contains(Body.Collider.Layer)&&CalculateBounds(Body).Intersects(Bounds)){
                Results.Add(new PhysicsOverlapHit(Body.Entity,StableId));
            }
        }
        return new PhysicsQueryResult(PhysicsQueryStatus.Success,Results.Count-Start);
    }

    public bool TryGetBodyState(Entity Entity,out PhysicsBodyState State){
        if(!this.StableIdByEntity.TryGetValue(Entity,out ulong StableId)||!this.Bodies.TryGetValue(StableId,out Body? Body)){
            State=default;
            return false;
        }
        State=new PhysicsBodyState(Body.Entity,StableId,Body.RigidBody?.MotionType,Body.Position,Body.Rotation,Body.Velocity,Body.IsSleeping,Body.SleepTicks);
        return true;
    }

    /// <summary>The surface material of a body (ADR-0078/M0109): a vehicle's tyre reads the friction of what its ray hit, as UE reads the hit's physical material.</summary>
    public bool TryGetMaterial(Entity Entity,out PhysicsMaterial Material){
        if(!this.StableIdByEntity.TryGetValue(Entity,out ulong StableId)||!this.Bodies.TryGetValue(StableId,out Body? Body)){
            Material=default;
            return false;
        }
        Material=Body.Collider.Material;
        return true;
    }

    public int CollectDebugBodies(List<PhysicsDebugBody> Results,int MaximumCount=8192){
        ArgumentNullException.ThrowIfNull(Results);
        if(MaximumCount<0){ throw new ArgumentOutOfRangeException(nameof(MaximumCount)); }
        int Start=Results.Count;
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            if(Results.Count-Start>=MaximumCount){ break; }
            Body Body=Pair.Value;
            Results.Add(new PhysicsDebugBody(Body.Entity.Id,Body.StableId,CalculateBounds(Body),Body.Position,Body.Velocity.Linear,Body.IsSleeping,Body.RigidBody?.MotionType));
        }
        return Results.Count-Start;
    }

    public int CollectContactManifolds(List<PhysicsContactManifold> Results,int MaximumCount=int.MaxValue){
        ArgumentNullException.ThrowIfNull(Results);
        if(MaximumCount<0){ throw new ArgumentOutOfRangeException(nameof(MaximumCount)); }
        int Start=Results.Count;
        foreach(KeyValuePair<(ulong First,ulong Second),CachedContact> Pair in this.ContactCache){
            if(Results.Count-Start>=MaximumCount){ break; }
            CachedContact Contact=Pair.Value;
            if(!Contact.Active||Contact.IsTrigger){ continue; }
            List<PhysicsContactPoint> Points=new(Contact.PointCount);
            for(int Index=0;Index<Contact.PointCount;Index++){
                CachedPoint Point=Contact.Points[Index];
                Points.Add(new PhysicsContactPoint(Point.Position,Point.Normal,Point.Penetration,Point.NormalImpulse));
            }
            Results.Add(new PhysicsContactManifold(
                BuildPairId(Pair.Key.First,Pair.Key.Second),Contact.First.Entity,Contact.Second.Entity,Points));
        }
        return Results.Count-Start;
    }

    internal int CollectBroadphaseDebugNodes(List<PhysicsDebugTreeNode>Results,int MaximumCount)=>
        this.Broadphase.CollectDebugNodes(Results,MaximumCount);

    internal int CollectBodyStates(List<PhysicsBodyState> Results){
        int Start=Results.Count;
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            Results.Add(new PhysicsBodyState(Body.Entity,Body.StableId,Body.RigidBody?.MotionType,Body.Position,Body.Rotation,Body.Velocity,Body.IsSleeping,Body.SleepTicks));
        }
        return Results.Count-Start;
    }

    internal bool TryApplyCacheFrame(in PhysicsCacheFrame Frame,out string Error){
        Error=string.Empty;
        if(Frame.Bodies.Count!=this.Bodies.Count){
            Error="Physics cache body set does not match the active world.";
            return false;
        }
        this.CacheValidationIds.Clear();
        foreach(PhysicsBodyState State in Frame.Bodies){
            if(!this.CacheValidationIds.Add(State.StableId)||!this.Bodies.TryGetValue(State.StableId,out Body? Body)
                ||Body.Entity!=State.Entity||Body.RigidBody?.MotionType!=State.MotionType
                ||!State.Rotation.TryNormalize(out _)){
                Error="Physics cache body identity or shape does not match the active world.";
                return false;
            }
            try { _=this.NarrowPhase.CalculateBounds(this.CreateShape(Body,State.Position,State.Rotation)); }
            catch(OverflowException){
                Error="Physics cache body pose exceeds the logical domain.";
                return false;
            }
        }
        foreach(PhysicsEvent Event in Frame.Events){
            if(Event.Tick!=Frame.Tick){
                Error="Physics cache event tick does not match its frame.";
                return false;
            }
        }
        foreach(var Break in Frame.Breaks){
            if(!PhysicsCacheSerializer.IsCanonicalBreak(Break)){
                Error="Physics cache bond break is not valid.";
                return false;
            }
        }
        foreach(var Strain in Frame.Strains){
            if(!PhysicsCacheSerializer.IsCanonicalStrain(Strain)){
                Error="Physics cache internal strain is not valid.";
                return false;
            }
        }
        foreach(PhysicsBodyState State in Frame.Bodies){
            Body Body=this.Bodies[State.StableId];
            Body.Position=State.Position;
            Body.Rotation=State.Rotation;
            Body.Velocity=State.Velocity;
            Body.IsSleeping=State.IsSleeping;
            Body.SleepTicks=State.SleepTicks;
            Body.Faulted=false;
            Body.WakeRequested=false;
            this.Broadphase.Upsert(Body.StableId,this.CalculateBounds(Body));
        }
        this.ContactCache.Clear();
        this.CcdContacts.Clear();
        this.MutableEvents.Clear();
        this.MutableEvents.AddRange(Frame.Events);
        this.MutableBondBreaks.Clear();
        this.MutableBondStrains.Clear();
        this.MutableBondBreaks.AddRange(Frame.Breaks);
        this.MutableBondStrains.AddRange(Frame.Strains);
        return true;
    }

    public ulong ComputeStateHash(){
        const ulong Offset=14695981039346656037UL;
        const ulong Prime=1099511628211UL;
        ulong Hash=Offset;
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            Hash=(Hash^Pair.Key)*Prime;
            Hash=HashScalar(Hash,Body.Position.R,Prime);
            Hash=HashScalar(Hash,Body.Position.U,Prime);
            Hash=HashScalar(Hash,Body.Position.F,Prime);
            Hash=HashScalar(Hash,Body.Velocity.Linear.R,Prime);
            Hash=HashScalar(Hash,Body.Velocity.Linear.U,Prime);
            Hash=HashScalar(Hash,Body.Velocity.Linear.F,Prime);
            Hash=(Hash^(Body.IsSleeping?1UL:0UL))*Prime;
        }
        return Hash;
    }

    private static ulong HashScalar(ulong Hash,LogicalScalar Value,ulong Prime){
        var Bits=unchecked((UInt128)Value.Raw);
        Hash=(Hash^(ulong)Bits)*Prime;
        return (Hash^(ulong)(Bits>>64))*Prime;
    }

    private void ApplyCommands(ulong Tick){
        this.CommittedCommands.Clear();
        this.Commands.Commit(Tick,this.CommittedCommands);
        foreach(SimulationCommand Envelope in this.CommittedCommands){
            PhysicsCommand Command=Envelope.Payload;
            if(!this.StableIdByEntity.TryGetValue(Command.Entity,out ulong StableId)||!this.Bodies.TryGetValue(StableId,out Body? Body)){ continue; }
            LogicalVector3 PreviousPosition=Body.Position;
            LogicalQuaternion PreviousRotation=Body.Rotation;
            PhysicsVelocity PreviousVelocity=Body.Velocity;
            try {
                switch(Command.Type){
                    case PhysicsCommandType.Force when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic } RigidBody:
                        Body.Velocity.Linear+=Command.Vector/RigidBody.Mass*FixedDelta;
                        Wake(Body,Tick,this.MutableEvents);
                        break;
                    case PhysicsCommandType.Impulse when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic } RigidBody:
                        Body.Velocity.Linear+=Command.Vector/RigidBody.Mass;
                        Wake(Body,Tick,this.MutableEvents);
                        break;
                    case PhysicsCommandType.ForceAtPoint when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic } RigidBody:
                        // ADR-0078/M0109: the force moves the centre of mass as Force does and turns the body by its moment about it.
                        Body.Velocity.Linear+=Command.Vector/RigidBody.Mass*FixedDelta;
                        Body.Velocity.Angular+=ApplyInverseInertia(Body,LogicalVector3.Cross(Command.Point-Body.Position,Command.Vector))*FixedDelta;
                        Wake(Body,Tick,this.MutableEvents);
                        break;
                    case PhysicsCommandType.Torque when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }:
                        Body.Velocity.Angular+=ApplyInverseInertia(Body,Command.Vector)*FixedDelta;
                        Wake(Body,Tick,this.MutableEvents);
                        break;
                    case PhysicsCommandType.SetVelocity when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }:
                        Body.Velocity=new PhysicsVelocity { Linear=Command.Vector,Angular=Command.Point };
                        Wake(Body,Tick,this.MutableEvents);
                        break;
                    case PhysicsCommandType.Sleep when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }:
                        if(!Body.IsSleeping){
                            // The tick's sleep update counts the last tick and reports the Sleep event, as for a body that came to rest by itself.
                            Body.IsSleeping=true;
                            Body.SleepTicks=59;
                            Body.Velocity=PhysicsVelocity.Zero;
                        }
                        break;
                    case PhysicsCommandType.Teleport when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }:
                    case PhysicsCommandType.KinematicTarget when Body.RigidBody is { MotionType:PhysicsMotionType.Kinematic }:
                        if(Command.Rotation.TryNormalize(out LogicalQuaternion Rotation)){
                            Body.Position=Command.Vector;
                            Body.Rotation=Rotation;
                            Wake(Body,Tick,this.MutableEvents);
                            this.Broadphase.Upsert(Body.StableId,this.CalculateBounds(Body));
                        }
                        else {
                            this.FaultBody(Body,Tick,PhysicsFaultType.InvalidQuaternion);
                        }
                        break;
                }
            }
            catch(OverflowException){
                Body.Position=PreviousPosition;
                Body.Rotation=PreviousRotation;
                Body.Velocity=PreviousVelocity;
                this.FaultBody(Body,Tick,PhysicsFaultType.NumericOverflow);
            }
        }
    }

    private LogicalVector3 IntegratePositionWithCcd(Body Body,LogicalVector3 Previous,LogicalVector3 Predicted){
        if(Body.RigidBody is not { Ccd:PhysicsCcdMode.ContinuousStatic }||Predicted==Previous){ return Predicted; }
        LogicalVector3 Displacement=Predicted-Previous;
        if(!LogicalScalar.TrySquareRoot(Displacement.LengthSquared,out LogicalScalar Distance)||Distance.IsZero
            ||!Displacement.TryNormalize(out LogicalVector3 Direction)){ return Predicted; }
        PhysicsAabb StartBounds=this.CalculateBounds(Body,Previous);
        PhysicsAabb EndBounds=this.CalculateBounds(Body,Predicted);
        PhysicsAabb Swept=Combine(StartBounds,EndBounds);
        LogicalVector3 StartCenter=(StartBounds.Min+StartBounds.Max)/LogicalScalar.FromInt64(2);
        LogicalVector3 HalfExtents=(StartBounds.Max-StartBounds.Min)/LogicalScalar.FromInt64(2);
        PhysicsRay Ray=new(StartCenter,Direction,Distance);
        this.QueryCandidates.Clear();
        this.Broadphase.Query(Swept,this.QueryCandidates);
        bool Found=false;
        LogicalScalar Earliest=Distance;
        LogicalVector3 HitNormal=default;
        Body? HitBody=null;
        foreach(ulong StableId in this.QueryCandidates){
            if(StableId==Body.StableId){ continue; }
            Body Other=this.Bodies[StableId];
            if(Other.RigidBody is not null||Other.Faulted||!CanCollide(Body,Other)){ continue; }
            PhysicsAabb Bounds=this.CalculateBounds(Other);
            PhysicsAabb Expanded=new(Bounds.Min-HalfExtents,Bounds.Max+HalfExtents);
            if(TryRayAabb(Ray,Expanded,out LogicalScalar HitDistance,out LogicalVector3 RayNormal)
                &&HitDistance<=Earliest){
                if(!Found||HitDistance<Earliest||StableId<HitBody!.StableId){
                    Found=true;
                    Earliest=HitDistance;
                    HitNormal=RayNormal;
                    HitBody=Other;
                }
            }
        }
        if(!Found||HitBody is null){ return Predicted; }
        LogicalScalar Travel=Max(LogicalScalar.Zero,Earliest-ContactSlop);
        LogicalVector3 Result=Previous+Direction*Travel;
        Body First=Body.StableId<HitBody.StableId?Body:HitBody;
        Body Second=ReferenceEquals(First,Body)?HitBody:Body;
        LogicalVector3 Normal=ReferenceEquals(First,Body)?-HitNormal:HitNormal;
        PhysicsContactCandidate Contact=new(
            StartCenter+Direction*Earliest,Normal,ContactSlop,uint.MaxValue);
        this.CcdContacts.Add(new CcdContact(First.StableId,Second.StableId,Contact));
        return Result;
    }

    private void BuildContacts(){
        foreach(CcdContact Contact in this.CcdContacts){
            if(this.Bodies.TryGetValue(Contact.First,out Body? First)&&this.Bodies.TryGetValue(Contact.Second,out Body? Second)){
                this.PatchScratch.Clear();
                this.PatchScratch.Add(Contact.Contact);
                this.AddContact(First,Second,this.PatchScratch);
            }
        }
        for(int BodyIndex=0;BodyIndex<this.Bodies.Count;BodyIndex++){
            Body Dynamic=this.Bodies.Values[BodyIndex];
            if(Dynamic.RigidBody is not { MotionType:PhysicsMotionType.Dynamic }||Dynamic.Faulted){ continue; }
            PhysicsAabb DynamicBounds=this.CalculateBounds(Dynamic);
            this.QueryCandidates.Clear();
            this.Broadphase.Query(DynamicBounds,this.QueryCandidates);
            foreach(ulong OtherId in this.QueryCandidates){
                if(OtherId==Dynamic.StableId){ continue; }
                Body Other=this.Bodies[OtherId];
                if(Other.Faulted||Other.RigidBody is { MotionType:PhysicsMotionType.Dynamic }&&Other.StableId<Dynamic.StableId
                    ||!CanCollide(Dynamic,Other)){ continue; }
                Body First=Dynamic.StableId<Other.StableId?Dynamic:Other;
                Body Second=ReferenceEquals(First,Dynamic)?Other:Dynamic;
                // ADR-0078/M0113: a sleeping body against a static or sleeping one has not moved since it fell asleep, so its contact holds as it was
                // (no narrow phase; the solver leaves it alone). A pile of fragments at rest costs nothing a tick.
                if(Dynamic.IsSleeping&&(Other.RigidBody is null||Other.RigidBody is { MotionType:PhysicsMotionType.Dynamic }&&Other.IsSleeping)){
                    if(this.TryGetContact((First.StableId,Second.StableId),out CachedContact Held)){ Held.Active=true; }
                    continue;
                }
                this.PatchScratch.Clear();
                if(this.NarrowPhase.TryCreateContacts(this.CreateShape(First),this.CreateShape(Second),this.PatchScratch)){
                    this.AddContact(First,Second,this.PatchScratch);
                }
            }
        }
    }

    /// <summary>
    /// Stores the patches of a pair's contact for this tick (M0124: one per face orientation, the deepest first). A patch whose feature and normal (within
    /// about 18 degrees) match a patch of the last tick takes over that patch's warm-started impulses slot by slot; any other starts from zero. A second
    /// contact of the same pair in one tick (a CCD contact, then the narrow phase's) replaces the first only when it is deeper.
    /// </summary>
    private void AddContact(Body First,Body Second,List<PhysicsContactCandidate>Candidates){
        (ulong First,ulong Second) Key=(First.StableId,Second.StableId);
        if(!this.TryGetContact(Key,out CachedContact Contact)){
            Contact=new CachedContact(First,Second);
            this.ContactCache.Add(Key,Contact);
        }
        var Deepest=LogicalScalar.Zero;
        foreach(var Candidate in Candidates){ if(Candidate.Penetration>Deepest){ Deepest=Candidate.Penetration; } }
        if(Contact.Active&&Contact.MaximumPenetration>=Deepest){ return; }
        Array.Copy(Contact.Points,Contact.Previous,Contact.Points.Length);
        var PreviousPatches=Contact.Patches;
        Span<bool>Taken=stackalloc bool[PhysicsNarrowPhase.MaximumContactPatches];
        Span<int>PreviousStart=stackalloc int[PhysicsNarrowPhase.MaximumContactPatches];
        Span<int>PreviousCount=stackalloc int[PhysicsNarrowPhase.MaximumContactPatches];
        Span<uint>PreviousFeature=stackalloc uint[PhysicsNarrowPhase.MaximumContactPatches];
        Span<LogicalVector3>PreviousNormal=stackalloc LogicalVector3[PhysicsNarrowPhase.MaximumContactPatches];
        for(var Patch=0;Patch<PreviousPatches;Patch++){
            PreviousStart[Patch]=Contact.PatchStart[Patch];
            PreviousCount[Patch]=Contact.PatchCount[Patch];
            PreviousFeature[Patch]=Contact.PatchFeature[Patch];
            PreviousNormal[Patch]=Contact.PatchNormal[Patch];
        }
        Array.Clear(Contact.Points);
        var Count=0;
        var Patches=0;
        foreach(var Candidate in Candidates){
            if(Patches==PhysicsNarrowPhase.MaximumContactPatches||Count+Candidate.Count>Contact.Points.Length){ break; }
            LogicalVector3 Normal=Candidate.Normal.TryNormalize(out LogicalVector3 Unit)?Unit:LogicalVector3.UnitU;
            var Match=-1;
            for(var Patch=0;Patch<PreviousPatches&&Match<0;Patch++){
                if(!Taken[Patch]&&PreviousFeature[Patch]==Candidate.FeatureId&&LogicalVector3.Dot(PreviousNormal[Patch],Normal)>=LogicalScalar.FromSingle(0.95f)){ Match=Patch; }
            }
            if(Match>=0){ Taken[Match]=true; }
            Contact.PatchStart[Patches]=Count;
            Contact.PatchCount[Patches]=Candidate.Count;
            Contact.PatchFeature[Patches]=Candidate.FeatureId;
            Contact.PatchNormal[Patches]=Normal;
            for(int Index=0;Index<Candidate.Count;Index++){
                ref CachedPoint Point=ref Contact.Points[Count+Index];
                if(Match>=0&&Index<PreviousCount[Match]){ Point=Contact.Previous[PreviousStart[Match]+Index]; }
                Point.Normal=Normal;
                Point.Position=Candidate.GetPosition(Index);
                Point.Penetration=Candidate.GetPenetration(Index);
            }
            Count+=Candidate.Count;
            Patches++;
        }
        Contact.PointCount=Count;
        Contact.Patches=Patches;
        Contact.IsTrigger=First.Collider.IsTrigger||Second.Collider.IsTrigger;
        Contact.Active=true;
    }

    /// <summary>
    /// ADR-0078/M0113: a sleeping body touched by an awake dynamic body wakes (it is being pushed); a contact with no awake dynamic body is not solved, so
    /// a body at rest keeps its pose and its sleep. The pass runs in contact order, so the same contacts wake the same bodies.
    /// </summary>
    private void SolveContacts(ulong Tick){
        for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
            CachedContact Contact=this.ContactCache.Values[ContactIndex];
            Contact.Solve=false;
            if(!Contact.Active||Contact.IsTrigger){ continue; }
            var FirstAwake=IsAwakeDynamic(Contact.First);
            var SecondAwake=IsAwakeDynamic(Contact.Second);
            if(FirstAwake&&Contact.Second.IsSleeping&&IsDynamic(Contact.Second)){ Wake(Contact.Second,Tick,this.MutableEvents);SecondAwake=true; }
            if(SecondAwake&&Contact.First.IsSleeping&&IsDynamic(Contact.First)){ Wake(Contact.First,Tick,this.MutableEvents);FirstAwake=true; }
            Contact.Solve=FirstAwake||SecondAwake;
        }
        for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
            CachedContact Contact=this.ContactCache.Values[ContactIndex];
            if(!Contact.Solve){ continue; }
            for(int PointIndex=0;PointIndex<Contact.PointCount;PointIndex++){ PrepareBounce(Contact,PointIndex); }
        }
        for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
            CachedContact Contact=this.ContactCache.Values[ContactIndex];
            if(!Contact.Solve){ continue; }
            for(int PointIndex=0;PointIndex<Contact.PointCount;PointIndex++){
                CachedPoint Point=Contact.Points[PointIndex];
                LogicalVector3 WarmImpulse=Point.Normal*Point.NormalImpulse+Point.Tangent*Point.TangentImpulse;
                ApplyImpulse(Contact,Point,WarmImpulse);
            }
        }
        for(int Iteration=0;Iteration<8;Iteration++){
            for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
                CachedContact Contact=this.ContactCache.Values[ContactIndex];
                if(!Contact.Solve){ continue; }
                for(int PointIndex=0;PointIndex<Contact.PointCount;PointIndex++){
                    SolveVelocity(Contact,PointIndex);
                }
            }
        }
        for(int Iteration=0;Iteration<3;Iteration++){
            for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
                CachedContact Contact=this.ContactCache.Values[ContactIndex];
                if(!Contact.Solve){ continue; }
                for(int PointIndex=0;PointIndex<Contact.PointCount;PointIndex++){ SolvePosition(Contact,PointIndex); }
            }
        }
    }

    /// <summary>
    /// Takes the point's bounce from its approach speed before any impulse of this step. Aiming every iteration at that one value keeps the points of a face-on impact
    /// equal; a bounce taken from the speed as the iterations change it differed point to point and spun the body. A speculative point (negative penetration: a corner a
    /// little above the face, ADR-0078/M0109) bounces only if it reaches the face in this tick.
    /// </summary>
    private static void PrepareBounce(CachedContact Contact,int PointIndex){
        ref CachedPoint Point=ref Contact.Points[PointIndex];
        LogicalVector3 Relative=PointVelocity(Contact.Second,Point.Position)-PointVelocity(Contact.First,Point.Position);
        LogicalScalar NormalVelocity=LogicalVector3.Dot(Relative,Point.Normal);
        LogicalScalar Restitution=Min(Contact.First.Collider.Material.Restitution,Contact.Second.Collider.Material.Restitution);
        LogicalScalar Gap=Point.Penetration<LogicalScalar.Zero?-Point.Penetration:LogicalScalar.Zero;
        // The impulse that stopped an impact is not what holds the body afterwards: warm-starting it would throw the body back off the surface at the impact speed, and the
        // iterations that take it out again leave the points' leftovers as a spin (a car at 30 m/s into a wall).
        if(Point.Impacted){
            Point.NormalImpulse=LogicalScalar.Zero;
            Point.TangentImpulse=LogicalScalar.Zero;
        }
        Point.Impacted=NormalVelocity<-RestitutionVelocityThreshold;
        Point.Reaching=Gap.IsZero||-NormalVelocity*FixedDelta>Gap;
        Point.Bounce=Point.Reaching&&NormalVelocity<-RestitutionVelocityThreshold?-NormalVelocity*Restitution:LogicalScalar.Zero;
    }

    private static void SolveVelocity(CachedContact Contact,int PointIndex){
        ref CachedPoint Point=ref Contact.Points[PointIndex];
        LogicalVector3 Relative=PointVelocity(Contact.Second,Point.Position)-PointVelocity(Contact.First,Point.Position);
        LogicalScalar NormalVelocity=LogicalVector3.Dot(Relative,Point.Normal);
        // A speculative point that does not reach the surface in this step is free; one that does is held like a touching point (a corner a few millimetres off the wall
        // that kept approaching would turn the body).
        LogicalScalar Target=Point.Reaching?Point.Bounce:Point.Penetration/FixedDelta;
        LogicalScalar Effective=EffectiveMass(Contact,Point,Point.Normal);
        if(Effective<=LogicalScalar.Zero){ return; }
        LogicalScalar Delta=(Target-NormalVelocity)/Effective;
        LogicalScalar Previous=Point.NormalImpulse;
        Point.NormalImpulse=Max(LogicalScalar.Zero,Previous+Delta);
        ApplyImpulse(Contact,Point,Point.Normal*(Point.NormalImpulse-Previous));

        Relative=PointVelocity(Contact.Second,Point.Position)-PointVelocity(Contact.First,Point.Position);
        LogicalVector3 TangentVelocity=Relative-Point.Normal*LogicalVector3.Dot(Relative,Point.Normal);
        if(!TangentVelocity.TryNormalize(out LogicalVector3 Tangent)){ return; }
        if(Point.Tangent.LengthSquared.IsZero||LogicalVector3.Dot(Point.Tangent,Tangent)<LogicalScalar.Zero){
            Point.Tangent=Tangent;
            Point.TangentImpulse=LogicalScalar.Zero;
        }
        LogicalScalar TangentEffective=EffectiveMass(Contact,Point,Point.Tangent);
        if(TangentEffective<=LogicalScalar.Zero){ return; }
        LogicalScalar Friction=Min(Contact.First.Collider.Material.Friction,Contact.Second.Collider.Material.Friction);
        LogicalScalar Maximum=Friction*Point.NormalImpulse;
        LogicalScalar TangentDelta=-LogicalVector3.Dot(Relative,Point.Tangent)/TangentEffective;
        LogicalScalar OldTangent=Point.TangentImpulse;
        Point.TangentImpulse=Clamp(OldTangent+TangentDelta,-Maximum,Maximum);
        ApplyImpulse(Contact,Point,Point.Tangent*(Point.TangentImpulse-OldTangent));
    }

    private static void SolvePosition(CachedContact Contact,int PointIndex){
        ref CachedPoint Point=ref Contact.Points[PointIndex];
        LogicalScalar Penetration=Max(LogicalScalar.Zero,Point.Penetration-ContactSlop);
        if(Penetration.IsZero){ return; }
        LogicalScalar FirstInverse=InverseMass(Contact.First);
        LogicalScalar SecondInverse=InverseMass(Contact.Second);
        LogicalScalar Total=FirstInverse+SecondInverse;
        if(Total.IsZero){ return; }
        LogicalScalar Correction=Penetration*PositionCorrection
            /LogicalScalar.FromInt64(checked(3*Contact.PointCount));
        LogicalVector3 Offset=Point.Normal*(Correction/Total);
        // ADR-0078/M0113: the correction moves the body but is not motion that keeps it awake: a body at rest is pushed out of its support every tick
        // (gravity sinks it by g·Δt² and this lifts it back), and counting that as motion kept every resting body awake for ever.
        if(!FirstInverse.IsZero){ Contact.First.Position-=Offset*FirstInverse; }
        if(!SecondInverse.IsZero){ Contact.Second.Position+=Offset*SecondInverse; }
        Point.Penetration=Max(ContactSlop,Point.Penetration-Correction);
    }

    private void PublishContactEvents(ulong Tick){
        this.ContactRemovalScratch.Clear();
        for(int ContactIndex=0;ContactIndex<this.ContactCache.Count;ContactIndex++){
            CachedContact Contact=this.ContactCache.Values[ContactIndex];
            (ulong First,ulong Second) Key=this.ContactCache.Keys[ContactIndex];
            ulong PairId=BuildPairId(Key.First,Key.Second);
            if(Contact.Active){
                if(Contact.IsTrigger){
                    if(!Contact.WasActive){ this.MutableEvents.Add(new PhysicsEvent(Tick,PairId,PhysicsEventType.TriggerBegin,Contact.First.Entity,Contact.Second.Entity,default,default)); }
                }
                else {
                    PhysicsEventType Type=Contact.WasActive?PhysicsEventType.ContactPersist:PhysicsEventType.ContactBegin;
                    this.MutableEvents.Add(new PhysicsEvent(Tick,PairId,Type,Contact.First.Entity,Contact.Second.Entity,Contact.TotalNormalImpulse,default));
                }
            }
            else {
                if(Contact.WasActive){
                    PhysicsEventType Type=Contact.IsTrigger?PhysicsEventType.TriggerEnd:PhysicsEventType.ContactEnd;
                    this.MutableEvents.Add(new PhysicsEvent(Tick,PairId,Type,Contact.First.Entity,Contact.Second.Entity,default,default));
                }
                this.ContactRemovalScratch.Add(Key);
            }
        }
        foreach((ulong First,ulong Second) Key in this.ContactRemovalScratch){ this.ContactCache.Remove(Key); }
    }

    private static void ApplyImpulse(CachedContact Contact,in CachedPoint Point,LogicalVector3 Impulse){
        LogicalScalar FirstInverse=InverseMass(Contact.First);
        if(!FirstInverse.IsZero){
            Contact.First.Velocity.Linear-=Impulse*FirstInverse;
            Contact.First.Velocity.Angular-=ApplyInverseInertia(Contact.First,LogicalVector3.Cross(Point.Position-Contact.First.Position,Impulse));
        }
        LogicalScalar SecondInverse=InverseMass(Contact.Second);
        if(!SecondInverse.IsZero){
            Contact.Second.Velocity.Linear+=Impulse*SecondInverse;
            Contact.Second.Velocity.Angular+=ApplyInverseInertia(Contact.Second,LogicalVector3.Cross(Point.Position-Contact.Second.Position,Impulse));
        }
    }

    private static LogicalScalar EffectiveMass(CachedContact Contact,in CachedPoint Point,LogicalVector3 Direction){
        LogicalScalar Result=InverseMass(Contact.First)+InverseMass(Contact.Second);
        LogicalVector3 FirstArm=Point.Position-Contact.First.Position;
        LogicalVector3 SecondArm=Point.Position-Contact.Second.Position;
        if(!InverseMass(Contact.First).IsZero){
            LogicalVector3 Angular=ApplyInverseInertia(Contact.First,LogicalVector3.Cross(FirstArm,Direction));
            Result+=LogicalVector3.Dot(LogicalVector3.Cross(Angular,FirstArm),Direction);
        }
        if(!InverseMass(Contact.Second).IsZero){
            LogicalVector3 Angular=ApplyInverseInertia(Contact.Second,LogicalVector3.Cross(SecondArm,Direction));
            Result+=LogicalVector3.Dot(LogicalVector3.Cross(Angular,SecondArm),Direction);
        }
        return Result;
    }

    private static LogicalVector3 ApplyInverseInertia(Body Body,LogicalVector3 WorldTorque){
        if(Body.RigidBody is not { MotionType:PhysicsMotionType.Dynamic,Mass:var Mass }){ return LogicalVector3.Zero; }
        LogicalVector3 Inertia=CalculateInertia(Body,Mass);
        LogicalQuaternion InverseRotation=Body.Rotation.Conjugate();
        if(!InverseRotation.TryRotate(WorldTorque,out LogicalVector3 Local)){ throw new OverflowException(); }
        Local=new LogicalVector3(
            Inertia.R.IsZero?LogicalScalar.Zero:Local.R/Inertia.R,
            Inertia.U.IsZero?LogicalScalar.Zero:Local.U/Inertia.U,
            Inertia.F.IsZero?LogicalScalar.Zero:Local.F/Inertia.F);
        return Body.Rotation.TryRotate(Local,out LogicalVector3 World)?World:throw new OverflowException();
    }

    private static LogicalVector3 CalculateInertia(Body Body,LogicalScalar Mass){
        if(Body.Collider.Shape==ColliderShape.Sphere){
            LogicalScalar Value=LogicalScalar.FromSingle(0.4f)*Mass*Body.Collider.Radius*Body.Collider.Radius;
            return new LogicalVector3(Value,Value,Value);
        }
        if(Body.Collider.Shape==ColliderShape.Capsule){
            LogicalScalar Radius=Body.Collider.Radius;
            LogicalScalar HalfHeight=Body.Collider.HalfHeight;
            LogicalScalar CylinderWeight=LogicalScalar.FromInt64(2)*Radius*Radius*HalfHeight;
            LogicalScalar SphereWeight=LogicalScalar.FromSingle(4f/3f)*Radius*Radius*Radius;
            LogicalScalar TotalWeight=CylinderWeight+SphereWeight;
            LogicalScalar CylinderMass=TotalWeight.IsZero?LogicalScalar.Zero:Mass*CylinderWeight/TotalWeight;
            LogicalScalar SphereMass=Mass-CylinderMass;
            LogicalScalar RadiusSquared=Radius*Radius;
            LogicalScalar Height=LogicalScalar.FromInt64(2)*HalfHeight;
            LogicalScalar Axis=LogicalScalar.FromSingle(0.5f)*CylinderMass*RadiusSquared
                +LogicalScalar.FromSingle(0.4f)*SphereMass*RadiusSquared;
            LogicalScalar Transverse=CylinderMass*(LogicalScalar.FromInt64(3)*RadiusSquared+Height*Height)
                /LogicalScalar.FromInt64(12)
                +LogicalScalar.FromSingle(0.4f)*SphereMass*RadiusSquared+SphereMass*HalfHeight*HalfHeight;
            return new LogicalVector3(Transverse,Axis,Transverse);
        }
        LogicalVector3 Half=Body.Asset is null
            ?Body.Collider.HalfExtents
            :(Body.Asset.Bounds.Max-Body.Asset.Bounds.Min)/LogicalScalar.FromInt64(2);
        LogicalScalar Third=LogicalScalar.One/LogicalScalar.FromInt64(3);
        return new LogicalVector3(
            Mass*Third*(Half.U*Half.U+Half.F*Half.F),
            Mass*Third*(Half.R*Half.R+Half.F*Half.F),
            Mass*Third*(Half.R*Half.R+Half.U*Half.U));
    }

    private static LogicalScalar DampingFactor(LogicalScalar Damping)=>Max(LogicalScalar.Zero,LogicalScalar.One-Damping*FixedDelta);

    private static LogicalVector3 PointVelocity(Body Body,LogicalVector3 Point)=>
        Body.Velocity.Linear+LogicalVector3.Cross(Body.Velocity.Angular,Point-Body.Position);

    private static LogicalScalar InverseMass(Body Body)=>
        Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic,Mass:var Mass }?LogicalScalar.One/Mass:LogicalScalar.Zero;

    private static LogicalQuaternion IntegrateRotation(LogicalQuaternion Rotation,LogicalVector3 AngularVelocity){
        if(AngularVelocity.LengthSquared.IsZero){ return Rotation; }
        if(!LogicalScalar.TrySquareRoot(AngularVelocity.LengthSquared,out LogicalScalar Speed)
            ||!AngularVelocity.TryNormalize(out LogicalVector3 Axis)
            ||!LogicalQuaternion.TryCreateAxisAngle(Axis,Speed*FixedDelta,out LogicalQuaternion Delta)
            ||!LogicalQuaternion.TryMultiply(Delta,Rotation,out LogicalQuaternion Candidate)
            ||!Candidate.TryNormalize(out LogicalQuaternion Result)){ throw new OverflowException(); }
        return Result;
    }

    private void UpdateSleep(Body Body,ulong Tick){
        if(Body.RigidBody is not { AllowSleep:true }){ Body.SleepTicks=0; return; }
        if(Body.Velocity.Linear.LengthSquared<=SleepLinearSquared&&Body.Velocity.Angular.LengthSquared<=SleepLinearSquared){
            if(Body.SleepTicks<60){ Body.SleepTicks++; }
            if(Body.SleepTicks==60){
                Body.IsSleeping=true;
                Body.Velocity=PhysicsVelocity.Zero;
                this.MutableEvents.Add(new PhysicsEvent(Tick,BuildPairId(Body.StableId,Body.StableId),PhysicsEventType.Sleep,Body.Entity,default,default,default));
            }
        }
        else {
            Body.SleepTicks=0;
        }
    }

    private void FaultBody(Body Body,ulong Tick,PhysicsFaultType Fault){
        Body.Faulted=true;
        Body.IsSleeping=true;
        Body.Velocity=PhysicsVelocity.Zero;
        this.MutableEvents.Add(new PhysicsEvent(Tick,BuildPairId(Body.StableId,Body.StableId),PhysicsEventType.Fault,Body.Entity,default,default,Fault));
    }

    private static void Wake(Body Body,ulong Tick,List<PhysicsEvent> Events){
        if(!Body.IsSleeping){ Body.SleepTicks=0; return; }
        Body.IsSleeping=false;
        Body.SleepTicks=0;
        Events.Add(new PhysicsEvent(Tick,BuildPairId(Body.StableId,Body.StableId),PhysicsEventType.Wake,Body.Entity,default,default,default));
    }

    private static bool IsDynamic(Body Body)=>Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }&&!Body.Faulted;
    /// <summary>A body that moves things it touches: an awake dynamic body, or a kinematic one (driven by its targets).</summary>
    private static bool IsAwakeDynamic(Body Body)=>IsDynamic(Body)&&!Body.IsSleeping||Body.RigidBody is { MotionType:PhysicsMotionType.Kinematic }&&!Body.Faulted;
    private PhysicsUpdateStatus ValidateAndCalculateBounds(
        in PhysicsBodyDescription Description,out PhysicsAabb Bounds,out PhysicsShapeAsset? Asset){
        Bounds=default;
        Asset=null;
        if(Description.StableId==0||Description.Collider.Layer.Value>=64){ return PhysicsUpdateStatus.InvalidInput; }
        if(!Description.Rotation.TryNormalize(out _)){ return PhysicsUpdateStatus.InvalidInput; }
        if(Description.Collider.Shape==ColliderShape.StaticTriangleMesh&&Description.RigidBody is not null){ return PhysicsUpdateStatus.InvalidShape; }
        if(Description.RigidBody is { Mass:var Mass }
            &&(Mass<=LogicalScalar.Zero||!LogicalScalar.TryDivide(LogicalScalar.One,Mass,out _))){
            return PhysicsUpdateStatus.InvalidMass;
        }
        if(!this.TryResolveAsset(Description.Collider,out Asset)){ return PhysicsUpdateStatus.InvalidShape; }
        if(Description.Collider.Shape==ColliderShape.Box
            &&(Description.Collider.HalfExtents.R<=LogicalScalar.Zero
                ||Description.Collider.HalfExtents.U<=LogicalScalar.Zero
                ||Description.Collider.HalfExtents.F<=LogicalScalar.Zero)
            ||Description.Collider.Shape==ColliderShape.Sphere&&Description.Collider.Radius<=LogicalScalar.Zero
            ||Description.Collider.Shape==ColliderShape.Capsule
                &&(Description.Collider.Radius<=LogicalScalar.Zero||Description.Collider.HalfHeight<LogicalScalar.Zero)){
            return PhysicsUpdateStatus.InvalidShape;
        }
        try {
            Bounds=this.NarrowPhase.CalculateBounds(new PhysicsCollisionShape(
                Description.Collider,Description.Position,Description.Rotation,Asset));
            return PhysicsUpdateStatus.Success;
        }
        catch(OverflowException){ return PhysicsUpdateStatus.NumericOverflow; }
    }

    private bool TryResolveAsset(in Collider Collider,out PhysicsShapeAsset? Asset){
        Asset=null;
        if(Collider.Shape is not (ColliderShape.ConvexAsset or ColliderShape.StaticTriangleMesh or ColliderShape.Compound)){ return true; }
        if(string.IsNullOrWhiteSpace(Collider.Asset)||!this.Shapes.TryGet(Collider.Asset,out PhysicsShapeAsset Resolved)){ return false; }
        if(Collider.Shape==ColliderShape.ConvexAsset&&Resolved.Kind!=PhysicsShapeAssetKind.Convex
            ||Collider.Shape==ColliderShape.StaticTriangleMesh&&Resolved.Kind!=PhysicsShapeAssetKind.StaticTriangleMesh
            ||Collider.Shape==ColliderShape.Compound&&Resolved.Kind!=PhysicsShapeAssetKind.Compound){ return false; }
        Asset=Resolved;
        return true;
    }

    private PhysicsCollisionShape CreateShape(Body Body)=>this.CreateShape(Body,Body.Position);

    private PhysicsCollisionShape CreateShape(Body Body,LogicalVector3 Position)=>this.CreateShape(Body,Position,Body.Rotation);

    private PhysicsCollisionShape CreateShape(Body Body,LogicalVector3 Position,LogicalQuaternion Rotation)=>
        new(Body.Collider,Position,Rotation,Body.Asset);

    private PhysicsAabb CalculateBounds(Body Body)=>this.NarrowPhase.CalculateBounds(this.CreateShape(Body));
    private PhysicsAabb CalculateBounds(Body Body,LogicalVector3 Position)=>this.NarrowPhase.CalculateBounds(this.CreateShape(Body,Position));

    private static LogicalVector3 ShapeHalfExtents(in Collider Collider)=>Collider.Shape switch {
        ColliderShape.Sphere=>new LogicalVector3(Collider.Radius,Collider.Radius,Collider.Radius),
        ColliderShape.Capsule=>new LogicalVector3(Collider.Radius,Collider.Radius+Collider.HalfHeight,Collider.Radius),
        _=>Collider.HalfExtents,
    };

    /// <summary>
    /// Drops the contacts of a body that was removed or changed. ADR-0078/M0113: a sleeping body is not integrated, so the other side of each dropped contact
    /// is asked to wake at the next step; otherwise a piece resting on a piece that expired, or on a destructible that came apart, hung in the air.
    /// </summary>
    private void RemoveContactsFor(ulong StableId){
        this.ContactRemovalScratch.Clear();
        foreach(KeyValuePair<(ulong First,ulong Second),CachedContact> Pair in this.ContactCache){
            if(Pair.Key.First!=StableId&&Pair.Key.Second!=StableId){ continue; }
            this.ContactRemovalScratch.Add(Pair.Key);
            Body Other=Pair.Key.First==StableId?Pair.Value.Second:Pair.Value.First;
            if(Other.StableId!=StableId&&Other.IsSleeping){ Other.WakeRequested=true; }
        }
        foreach((ulong First,ulong Second) Key in this.ContactRemovalScratch){ this.ContactCache.Remove(Key); }
        this.ContactRemovalScratch.Clear();
    }

    private static bool CanCollide(Body First,Body Second)=>
        First.Collider.CollidesWith.Contains(Second.Collider.Layer)
        &&Second.Collider.CollidesWith.Contains(First.Collider.Layer);

    private bool TryGetContact((ulong First,ulong Second) Key,out CachedContact Contact){
        int Minimum=0;
        int Maximum=this.ContactCache.Count-1;
        while(Minimum<=Maximum){
            int Index=Minimum+(Maximum-Minimum)/2;
            (ulong First,ulong Second) Candidate=this.ContactCache.Keys[Index];
            int Result=Candidate.First.CompareTo(Key.First);
            if(Result==0){ Result=Candidate.Second.CompareTo(Key.Second); }
            if(Result==0){
                Contact=this.ContactCache.Values[Index];
                return true;
            }
            if(Result<0){ Minimum=Index+1; }
            else { Maximum=Index-1; }
        }
        Contact=null!;
        return false;
    }

    private static PhysicsAabb Combine(in PhysicsAabb Left,in PhysicsAabb Right)=>new(
        new LogicalVector3(Min(Left.Min.R,Right.Min.R),Min(Left.Min.U,Right.Min.U),Min(Left.Min.F,Right.Min.F)),
        new LogicalVector3(Max(Left.Max.R,Right.Max.R),Max(Left.Max.U,Right.Max.U),Max(Left.Max.F,Right.Max.F)));

    private static bool TryRayAabb(in PhysicsRay Ray,in PhysicsAabb Bounds,out LogicalScalar Distance,out LogicalVector3 Normal){
        LogicalScalar Near=LogicalScalar.Zero;
        LogicalScalar Far=Ray.MaxDistance;
        Normal=default;
        if(!ClipAxis(Ray.Origin.R,Ray.Direction.R,Bounds.Min.R,Bounds.Max.R,LogicalVector3.UnitR,ref Near,ref Far,ref Normal)
            ||!ClipAxis(Ray.Origin.U,Ray.Direction.U,Bounds.Min.U,Bounds.Max.U,LogicalVector3.UnitU,ref Near,ref Far,ref Normal)
            ||!ClipAxis(Ray.Origin.F,Ray.Direction.F,Bounds.Min.F,Bounds.Max.F,LogicalVector3.UnitF,ref Near,ref Far,ref Normal)){
            Distance=default;
            return false;
        }
        Distance=Near;
        return true;
    }

    private static bool ClipAxis(LogicalScalar Origin,LogicalScalar Direction,LogicalScalar Minimum,LogicalScalar Maximum,LogicalVector3 Axis,ref LogicalScalar Near,ref LogicalScalar Far,ref LogicalVector3 Normal){
        if(Direction.IsZero){ return Origin>=Minimum&&Origin<=Maximum; }
        LogicalScalar First=(Minimum-Origin)/Direction;
        LogicalScalar Second=(Maximum-Origin)/Direction;
        LogicalVector3 CandidateNormal=Direction>LogicalScalar.Zero?-Axis:Axis;
        if(First>Second){ (First,Second)=(Second,First); }
        if(First>Near){ Near=First; Normal=CandidateNormal; }
        if(Second<Far){ Far=Second; }
        return Near<=Far&&Far>=LogicalScalar.Zero;
    }

    private static ulong BuildPairId(ulong First,ulong Second){
        if(First>Second){ (First,Second)=(Second,First); }
        ulong Hash=14695981039346656037UL;
        Hash=(Hash^First)*1099511628211UL;
        return (Hash^Second)*1099511628211UL;
    }
    private static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    private static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
    private static LogicalScalar Clamp(LogicalScalar Value,LogicalScalar Minimum,LogicalScalar Maximum)=>
        Value<Minimum?Minimum:Value>Maximum?Maximum:Value;

    private sealed class PhysicsRayHitComparer:IComparer<PhysicsRayHit> {
        internal static readonly PhysicsRayHitComparer Instance=new();
        public int Compare(PhysicsRayHit Left,PhysicsRayHit Right){
            int Result=Left.Distance.CompareTo(Right.Distance);
            return Result!=0?Result:Left.StableId.CompareTo(Right.StableId);
        }
    }
}
