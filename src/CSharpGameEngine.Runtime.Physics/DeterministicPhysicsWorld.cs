using CSharpGameEngine.Entities;
using SimulationCommand=CSharpGameEngine.Runtime.Simulation.SimulationCommand<CSharpGameEngine.Runtime.Physics.PhysicsCommand>;

namespace CSharpGameEngine.Runtime.Physics;

/// <summary>Deterministic single-writer sequential rigid-body world for a local meter domain.</summary>
public sealed class DeterministicPhysicsWorld:IReadOnlyPhysicsWorld {
    private sealed class Body(in PhysicsBodyDescription Description) {
        internal readonly Entity Entity=Description.Entity;
        internal readonly ulong StableId=Description.StableId;
        internal Collider Collider=Description.Collider;
        internal RigidBody? RigidBody=Description.RigidBody;
        internal PhysicsVector3 Position=Description.Position;
        internal PhysicsQuaternion Rotation=Description.Rotation;
        internal PhysicsVelocity Velocity=Description.Velocity;
        internal readonly PhysicsVector3 InitialPosition=Description.Position;
        internal readonly PhysicsQuaternion InitialRotation=Description.Rotation;
        internal readonly PhysicsVelocity InitialVelocity=Description.Velocity;
        internal bool IsSleeping;
        internal ushort SleepTicks;
        internal bool Faulted;
    }

    private static readonly PhysicsScalar FixedDelta=PhysicsScalar.FromDouble(1d/60d);
    private static readonly PhysicsVector3 Gravity=new(0,PhysicsScalar.FromDouble(-9.80665),0);
    private static readonly PhysicsScalar SleepLinearSquared=PhysicsScalar.FromDouble(0.0001);
    private readonly SortedDictionary<ulong,Body> Bodies=[];
    private readonly Dictionary<Entity,ulong> StableIdByEntity=[];
    private readonly DeterministicAabbTree Broadphase=new();
    private readonly List<ulong> QueryCandidates=[];
    private readonly List<SimulationCommand> CommittedCommands=[];
    private readonly List<PhysicsRayHit> SweepRayHits=[];
    private readonly List<PhysicsEvent> MutableEvents=[];

    public int BodyCount=>this.Bodies.Count;
    public IReadOnlyList<PhysicsEvent> Events=>this.MutableEvents;
    public PhysicsCommands Commands { get; }=new();
    public Logical.Vector3 DomainOrigin { get;set; }=Logical.Vector3.Zero;

    public PhysicsUpdateStatus Upsert(in PhysicsBodyDescription Description){
        PhysicsUpdateStatus Status=Validate(Description);
        if(Status!=PhysicsUpdateStatus.Success){ return Status; }
        if(this.StableIdByEntity.TryGetValue(Description.Entity,out ulong OldStableId)&&OldStableId!=Description.StableId){
            this.Remove(Description.Entity);
        }
        if(this.Bodies.TryGetValue(Description.StableId,out Body? Body)){
            Body.Collider=Description.Collider;
            Body.RigidBody=Description.RigidBody;
            Body.Position=Description.Position;
            Body.Rotation=Description.Rotation;
            Body.Velocity=Description.Velocity;
            Body.Faulted=false;
        }
        else {
            Body=new Body(Description);
            this.Bodies.Add(Description.StableId,Body);
        }
        this.StableIdByEntity[Description.Entity]=Description.StableId;
        this.Broadphase.Upsert(Description.StableId,CalculateBounds(Body));
        return PhysicsUpdateStatus.Success;
    }

    internal PhysicsUpdateStatus Synchronize(in PhysicsBodyDescription Description){
        PhysicsUpdateStatus Status=Validate(Description);
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
        Body.Collider=Description.Collider;
        Body.RigidBody=Description.RigidBody;
        if(CurrentMotionType is null||CurrentMotionType!=PreviousMotionType){
            Body.Position=Description.Position;
            Body.Rotation=Description.Rotation;
            Body.Velocity=Description.Velocity;
            Body.IsSleeping=false;
            Body.SleepTicks=0;
        }
        Body.Faulted=false;
        this.StableIdByEntity[Description.Entity]=Description.StableId;
        this.Broadphase.Upsert(Description.StableId,CalculateBounds(Body));
        return PhysicsUpdateStatus.Success;
    }

    public bool Remove(Entity Entity){
        if(!this.StableIdByEntity.Remove(Entity,out ulong StableId)){ return false; }
        this.Broadphase.Remove(StableId);
        this.Bodies.Remove(StableId);
        return true;
    }

    public void Clear(){
        this.Bodies.Clear();
        this.StableIdByEntity.Clear();
        this.Broadphase.Clear();
        this.MutableEvents.Clear();
    }

    internal void Reset(ulong Tick){
        this.MutableEvents.Clear();
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            Body.Position=Body.InitialPosition;
            Body.Rotation=Body.InitialRotation;
            Body.Velocity=Body.InitialVelocity;
            Body.IsSleeping=false;
            Body.SleepTicks=0;
            Body.Faulted=false;
            this.Broadphase.Upsert(Body.StableId,CalculateBounds(Body));
        }
    }
    public void Step(ulong Tick){
        this.MutableEvents.Clear();
        this.ApplyCommands(Tick);
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            if(Body.Faulted||Body.IsSleeping||Body.RigidBody is not { MotionType:PhysicsMotionType.Dynamic } RigidBody){ continue; }
            PhysicsVector3 PreviousPosition=Body.Position;
            PhysicsVelocity PreviousVelocity=Body.Velocity;
            try {
                PhysicsVector3 Acceleration=Gravity*RigidBody.GravityFactor;
                Body.Velocity.Linear+=Acceleration*FixedDelta;
                Body.Position+=Body.Velocity.Linear*FixedDelta;
                this.UpdateSleep(Body,Tick);
                this.Broadphase.Upsert(Body.StableId,CalculateBounds(Body));
            }
            catch(OverflowException){
                Body.Position=PreviousPosition;
                Body.Velocity=PreviousVelocity;
                this.FaultBody(Body,Tick,PhysicsFaultType.NumericOverflow);
            }
        }
        this.SolveBoxContacts(Tick);
        this.MutableEvents.Sort(static(Left,Right)=>{
            int Result=Left.Tick.CompareTo(Right.Tick);
            if(Result!=0){ return Result; }
            Result=Left.PairId.CompareTo(Right.PairId);
            return Result!=0?Result:Left.Type.CompareTo(Right.Type);
        });
    }

    public PhysicsQueryResult Raycast(in PhysicsRay Ray,PhysicsLayerMask Layers,List<PhysicsRayHit> Results){
        ArgumentNullException.ThrowIfNull(Results);
        if(Ray.MaxDistance<PhysicsScalar.Zero||Ray.Direction.LengthSquared.IsZero){
            return new PhysicsQueryResult(PhysicsQueryStatus.InvalidInput,0);
        }
        int Start=Results.Count;
        try {
            PhysicsVector3 End=Ray.Origin+Ray.Direction*Ray.MaxDistance;
            PhysicsAabb RayBounds=new(
                new PhysicsVector3(Min(Ray.Origin.R,End.R),Min(Ray.Origin.U,End.U),Min(Ray.Origin.F,End.F)),
                new PhysicsVector3(Max(Ray.Origin.R,End.R),Max(Ray.Origin.U,End.U),Max(Ray.Origin.F,End.F)));
            this.QueryCandidates.Clear();
            this.Broadphase.Query(RayBounds,this.QueryCandidates);
            foreach(ulong StableId in this.QueryCandidates){
                Body Body=this.Bodies[StableId];
                if(!Layers.Contains(Body.Collider.Layer)){ continue; }
                if(TryRayAabb(Ray,CalculateBounds(Body),out PhysicsScalar Distance,out PhysicsVector3 Normal)){
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

    public PhysicsQueryResult Sweep(in Collider Shape,in PhysicsVector3 Origin,in PhysicsVector3 Direction,PhysicsScalar Distance,PhysicsLayerMask Layers,List<PhysicsSweepHit> Results){
        ArgumentNullException.ThrowIfNull(Results);
        if(Distance<PhysicsScalar.Zero||Direction.LengthSquared.IsZero||Shape.Shape is ColliderShape.StaticTriangleMesh or ColliderShape.Compound){
            return new PhysicsQueryResult(PhysicsQueryStatus.InvalidInput,0);
        }
        int Start=Results.Count;
        PhysicsVector3 HalfExtents=ShapeHalfExtents(Shape);
        PhysicsRay Ray=new(Origin,Direction,Distance);
        this.SweepRayHits.Clear();
        PhysicsQueryResult RayResult=this.Raycast(Ray,Layers,this.SweepRayHits);
        if(RayResult.Status!=PhysicsQueryStatus.Success){ return new PhysicsQueryResult(RayResult.Status,0); }
        foreach(PhysicsRayHit Hit in this.SweepRayHits){
            PhysicsScalar ExpandedDistance=Max(PhysicsScalar.Zero,Hit.Distance-Max(HalfExtents.R,Max(HalfExtents.U,HalfExtents.F)));
            Results.Add(new PhysicsSweepHit(Hit.Entity,Hit.StableId,ExpandedDistance,Ray.Origin+Ray.Direction*ExpandedDistance,Hit.Normal));
        }
        return new PhysicsQueryResult(PhysicsQueryStatus.Success,Results.Count-Start);
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

    internal int CollectBodyStates(List<PhysicsBodyState> Results){
        int Start=Results.Count;
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            Results.Add(new PhysicsBodyState(Body.Entity,Body.StableId,Body.RigidBody?.MotionType,Body.Position,Body.Rotation,Body.Velocity,Body.IsSleeping,Body.SleepTicks));
        }
        return Results.Count-Start;
    }

    public ulong ComputeStateHash(){
        const ulong Offset=14695981039346656037UL;
        const ulong Prime=1099511628211UL;
        ulong Hash=Offset;
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Body=Pair.Value;
            Hash=(Hash^Pair.Key)*Prime;
            Hash=(Hash^(ulong)Body.Position.R.RawValue)*Prime;
            Hash=(Hash^(ulong)Body.Position.U.RawValue)*Prime;
            Hash=(Hash^(ulong)Body.Position.F.RawValue)*Prime;
            Hash=(Hash^(ulong)Body.Velocity.Linear.R.RawValue)*Prime;
            Hash=(Hash^(ulong)Body.Velocity.Linear.U.RawValue)*Prime;
            Hash=(Hash^(ulong)Body.Velocity.Linear.F.RawValue)*Prime;
            Hash=(Hash^(Body.IsSleeping?1UL:0UL))*Prime;
        }
        return Hash;
    }

    private void ApplyCommands(ulong Tick){
        this.CommittedCommands.Clear();
        this.Commands.Commit(Tick,this.CommittedCommands);
        foreach(SimulationCommand Envelope in this.CommittedCommands){
            PhysicsCommand Command=Envelope.Payload;
            if(!this.StableIdByEntity.TryGetValue(Command.Entity,out ulong StableId)||!this.Bodies.TryGetValue(StableId,out Body? Body)){ continue; }
            switch(Command.Type){
                case PhysicsCommandType.Force when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic } RigidBody:
                    Body.Velocity.Linear+=Command.Vector/RigidBody.Mass*FixedDelta;
                    Wake(Body,Tick,this.MutableEvents);
                    break;
                case PhysicsCommandType.Impulse when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic } RigidBody:
                    Body.Velocity.Linear+=Command.Vector/RigidBody.Mass;
                    Wake(Body,Tick,this.MutableEvents);
                    break;
                case PhysicsCommandType.Teleport when Body.RigidBody is { MotionType:PhysicsMotionType.Dynamic }:
                case PhysicsCommandType.KinematicTarget when Body.RigidBody is { MotionType:PhysicsMotionType.Kinematic }:
                    if(Command.Rotation.TryNormalize(out PhysicsQuaternion Rotation)){
                        Body.Position=Command.Vector;
                        Body.Rotation=Rotation;
                        Wake(Body,Tick,this.MutableEvents);
                        this.Broadphase.Upsert(Body.StableId,CalculateBounds(Body));
                    }
                    else {
                        this.FaultBody(Body,Tick,PhysicsFaultType.InvalidQuaternion);
                    }
                    break;
            }
        }
    }

    private void SolveBoxContacts(ulong Tick){
        this.QueryCandidates.Clear();
        foreach(KeyValuePair<ulong,Body> Pair in this.Bodies){
            Body Dynamic=Pair.Value;
            if(Dynamic.RigidBody is not { MotionType:PhysicsMotionType.Dynamic }||Dynamic.Faulted){ continue; }
            PhysicsAabb DynamicBounds=CalculateBounds(Dynamic);
            this.QueryCandidates.Clear();
            this.Broadphase.Query(DynamicBounds,this.QueryCandidates);
            foreach(ulong OtherId in this.QueryCandidates){
                if(OtherId==Dynamic.StableId){ continue; }
                Body Other=this.Bodies[OtherId];
                if(Other.RigidBody is { MotionType:PhysicsMotionType.Dynamic }&&Other.StableId<Dynamic.StableId){ continue; }
                if(Dynamic.Collider.IsTrigger||Other.Collider.IsTrigger){ continue; }
                PhysicsAabb OtherBounds=CalculateBounds(Other);
                if(!DynamicBounds.Intersects(OtherBounds)){ continue; }
                PhysicsScalar UpPenetration=OtherBounds.Max.U-DynamicBounds.Min.U;
                PhysicsScalar DownPenetration=DynamicBounds.Max.U-OtherBounds.Min.U;
                if(UpPenetration<=DownPenetration&&Dynamic.Velocity.Linear.U<PhysicsScalar.Zero){
                    Dynamic.Position=new PhysicsVector3(Dynamic.Position.R,Dynamic.Position.U+UpPenetration,Dynamic.Position.F);
                    Dynamic.Velocity.Linear=new PhysicsVector3(Dynamic.Velocity.Linear.R,-Dynamic.Velocity.Linear.U*Dynamic.Collider.Material.Restitution,Dynamic.Velocity.Linear.F);
                    this.Broadphase.Upsert(Dynamic.StableId,CalculateBounds(Dynamic));
                    ulong PairId=BuildPairId(Dynamic.StableId,Other.StableId);
                    this.MutableEvents.Add(new PhysicsEvent(Tick,PairId,PhysicsEventType.ContactPersist,Dynamic.Entity,Other.Entity,PhysicsScalar.Zero,default));
                    Wake(Dynamic,Tick,this.MutableEvents);
                    DynamicBounds=CalculateBounds(Dynamic);
                }
            }
        }
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

    private static PhysicsUpdateStatus Validate(in PhysicsBodyDescription Description){
        if(Description.StableId==0||Description.Collider.Layer.Value>=64){ return PhysicsUpdateStatus.InvalidInput; }
        if(!Description.Rotation.TryNormalize(out _)){ return PhysicsUpdateStatus.InvalidInput; }
        if(Description.Collider.Shape==ColliderShape.StaticTriangleMesh&&Description.RigidBody is not null){ return PhysicsUpdateStatus.InvalidShape; }
        if(Description.Collider.Shape is ColliderShape.ConvexAsset or ColliderShape.Compound or ColliderShape.StaticTriangleMesh
            &&string.IsNullOrWhiteSpace(Description.Collider.Asset)){ return PhysicsUpdateStatus.InvalidShape; }
        if(Description.RigidBody is { Mass:var Mass }&&Mass<=PhysicsScalar.Zero){ return PhysicsUpdateStatus.InvalidMass; }
        PhysicsVector3 Extents=ShapeHalfExtents(Description.Collider);
        return Extents.R<PhysicsScalar.Zero||Extents.U<PhysicsScalar.Zero||Extents.F<PhysicsScalar.Zero?PhysicsUpdateStatus.InvalidShape:PhysicsUpdateStatus.Success;
    }

    private static PhysicsAabb CalculateBounds(Body Body)=>PhysicsAabb.FromCenterHalfExtents(Body.Position+Body.Collider.Center,ShapeHalfExtents(Body.Collider));
    private static PhysicsVector3 ShapeHalfExtents(in Collider Collider)=>Collider.Shape switch {
        ColliderShape.Sphere=>new PhysicsVector3(Collider.Radius,Collider.Radius,Collider.Radius),
        ColliderShape.Capsule=>new PhysicsVector3(Collider.Radius,Collider.Radius+Collider.HalfHeight,Collider.Radius),
        _=>Collider.HalfExtents,
    };

    private static bool TryRayAabb(in PhysicsRay Ray,in PhysicsAabb Bounds,out PhysicsScalar Distance,out PhysicsVector3 Normal){
        PhysicsScalar Near=PhysicsScalar.Zero;
        PhysicsScalar Far=Ray.MaxDistance;
        Normal=default;
        if(!ClipAxis(Ray.Origin.R,Ray.Direction.R,Bounds.Min.R,Bounds.Max.R,PhysicsVector3.UnitR,ref Near,ref Far,ref Normal)
            ||!ClipAxis(Ray.Origin.U,Ray.Direction.U,Bounds.Min.U,Bounds.Max.U,PhysicsVector3.UnitU,ref Near,ref Far,ref Normal)
            ||!ClipAxis(Ray.Origin.F,Ray.Direction.F,Bounds.Min.F,Bounds.Max.F,PhysicsVector3.UnitF,ref Near,ref Far,ref Normal)){
            Distance=default;
            return false;
        }
        Distance=Near;
        return true;
    }

    private static bool ClipAxis(PhysicsScalar Origin,PhysicsScalar Direction,PhysicsScalar Minimum,PhysicsScalar Maximum,PhysicsVector3 Axis,ref PhysicsScalar Near,ref PhysicsScalar Far,ref PhysicsVector3 Normal){
        if(Direction.IsZero){ return Origin>=Minimum&&Origin<=Maximum; }
        PhysicsScalar First=(Minimum-Origin)/Direction;
        PhysicsScalar Second=(Maximum-Origin)/Direction;
        PhysicsVector3 CandidateNormal=Direction>PhysicsScalar.Zero?-Axis:Axis;
        if(First>Second){ (First,Second)=(Second,First); }
        if(First>Near){ Near=First; Normal=CandidateNormal; }
        if(Second<Far){ Far=Second; }
        return Near<=Far&&Far>=PhysicsScalar.Zero;
    }

    private static ulong BuildPairId(ulong First,ulong Second){
        if(First>Second){ (First,Second)=(Second,First); }
        ulong Hash=14695981039346656037UL;
        Hash=(Hash^First)*1099511628211UL;
        return (Hash^Second)*1099511628211UL;
    }
    private static PhysicsScalar Min(PhysicsScalar Left,PhysicsScalar Right)=>Left<Right?Left:Right;
    private static PhysicsScalar Max(PhysicsScalar Left,PhysicsScalar Right)=>Left>Right?Left:Right;

    private sealed class PhysicsRayHitComparer:IComparer<PhysicsRayHit> {
        internal static readonly PhysicsRayHitComparer Instance=new();
        public int Compare(PhysicsRayHit Left,PhysicsRayHit Right){
            int Result=Left.Distance.CompareTo(Right.Distance);
            return Result!=0?Result:Left.StableId.CompareTo(Right.StableId);
        }
    }
}
