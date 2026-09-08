using CSharpGameEngine.Entities;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.World;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class PhysicsWorldTests {
    [Fact]
    public void NarrowPhase_DoesNotReportContactForSeparatedRotatedBoxesWhoseAabbsOverlap(){
        DeterministicPhysicsWorld World=new();
        LogicalQuaternion Rotation=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,0,LogicalScalar.FromSingle(0.7853982f)));
        Collider Shape=new() { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(2,0.1f,0.1f) };
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,Shape,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One },
            LogicalVector3.Zero,Rotation,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,Shape,null,new LogicalVector3(-0.22f,0.22f,0),Rotation,PhysicsVelocity.Zero)));

        World.Step(1);

        Assert.DoesNotContain(World.Events,Event=>Event.Type is PhysicsEventType.ContactBegin or PhysicsEventType.ContactPersist);
    }

    [Fact]
    public void ContactPipeline_PublishesBoundedManifoldAndWarmStartsPersistentContact(){
        DeterministicPhysicsWorld World=new();
        Collider Shape=new() { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(0.5f,0.5f,0.5f) };
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,Shape,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One },
            new LogicalVector3(0,0.75f,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,Shape,null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));

        World.Step(1);
        List<PhysicsContactManifold> First=[];
        Assert.Equal(1,World.CollectContactManifolds(First));
        Assert.Equal(4,First[0].Points.Count);
        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.ContactBegin);

        World.Step(2);
        List<PhysicsContactManifold> Second=[];
        Assert.Equal(1,World.CollectContactManifolds(Second));
        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.ContactPersist);
        Assert.Contains(Second[0].Points,Point=>Point.AccumulatedNormalImpulse>LogicalScalar.Zero);
    }

    [Fact]
    public void DebugExtraction_IsBoundedAndUsesTheAuthoritativeBroadphaseTree(){
        DeterministicPhysicsWorld World=new();
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(PhysicsBodyDescription.CreateStaticBox(
            new Entity(1),new LogicalVector3(-3,0,0),new LogicalVector3(1,1,1))));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(PhysicsBodyDescription.CreateStaticBox(
            new Entity(2),new LogicalVector3(3,0,0),new LogicalVector3(1,1,1))));

        List<PhysicsDebugTreeNode> Nodes=[];
        Assert.Equal(3,World.CollectBroadphaseDebugNodes(Nodes,3));

        Assert.False(Nodes[0].IsLeaf);
        Assert.Equal(0,Nodes[0].Depth);
        Assert.Equal(new ulong[]{1,2},Nodes.Where(static Node=>Node.IsLeaf).Select(static Node=>Node.StableId));
        List<PhysicsDebugTreeNode> Bounded=[];
        Assert.Equal(1,World.CollectBroadphaseDebugNodes(Bounded,1));
        Assert.Single(Bounded);
        Assert.Throws<ArgumentOutOfRangeException>(()=>World.CollectBroadphaseDebugNodes([], -1));
    }

    [Fact]
    public void ContactDebugExtraction_RespectsZeroBudget(){
        DeterministicPhysicsWorld World=new();
        List<PhysicsContactManifold> Contacts=[];

        Assert.Equal(0,World.CollectContactManifolds(Contacts,0));
        Assert.Empty(Contacts);
        Assert.Throws<ArgumentOutOfRangeException>(()=>World.CollectContactManifolds([], -1));
    }

    [Fact]
    public void ContactLifecycle_PublishesBeginPersistAndEndExactlyOnce(){
        DeterministicPhysicsWorld World=new();
        Collider Shape=new() { HalfExtents=new LogicalVector3(0.5f,0.5f,0.5f) };
        Entity Dynamic=new(1);
        World.Upsert(new PhysicsBodyDescription(
            Dynamic,1,Shape,new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            new LogicalVector3(0,0.75f,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero));
        World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,Shape,null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));

        World.Step(1);
        Assert.Single(World.Events,Event=>Event.Type==PhysicsEventType.ContactBegin);
        World.Step(2);
        Assert.Single(World.Events,Event=>Event.Type==PhysicsEventType.ContactPersist);
        World.Commands.Teleport(3,99,0,Dynamic,new LogicalVector3(0,10,0),LogicalQuaternion.Identity);
        World.Step(3);
        Assert.Single(World.Events,Event=>Event.Type==PhysicsEventType.ContactEnd);
        World.Step(4);
        Assert.DoesNotContain(World.Events,Event=>Event.Type==PhysicsEventType.ContactEnd);
    }

    [Fact]
    public void TriggerLifecycle_DoesNotRunTheImpulseSolver(){
        DeterministicPhysicsWorld World=new();
        Collider Trigger=new() { IsTrigger=true,HalfExtents=new LogicalVector3(1,1,1) };
        Collider DynamicShape=new() { HalfExtents=new LogicalVector3(0.5f,0.5f,0.5f) };
        Entity Dynamic=new(1);
        World.Upsert(new PhysicsBodyDescription(
            Dynamic,1,DynamicShape,new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
        World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,Trigger,null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));

        World.Step(1);

        Assert.Single(World.Events,Event=>Event.Type==PhysicsEventType.TriggerBegin);
        Assert.DoesNotContain(World.Events,Event=>Event.Type is PhysicsEventType.ContactBegin or PhysicsEventType.ContactPersist);
        Assert.True(World.TryGetBodyState(Dynamic,out PhysicsBodyState State));
        Assert.Equal(LogicalVector3.Zero,State.Position);
    }

    [Fact]
    public void ConvexAssets_UseSupportMappedNarrowPhase(){
        DeterministicPhysicsWorld World=new();
        PhysicsShapeAsset Tetrahedron=PhysicsShapeAsset.CreateConvex(
            "tetra",
            [new LogicalVector3(1,1,1),new LogicalVector3(-1,-1,1),new LogicalVector3(-1,1,-1),new LogicalVector3(1,-1,-1)]);
        World.Shapes.Add(Tetrahedron);
        Collider Convex=new() { Shape=ColliderShape.ConvexAsset,Asset="tetra" };
        World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,Convex,new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
        World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,Convex,null,new LogicalVector3(0.25f,0,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero));

        World.Step(1);

        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.ContactBegin);
    }

    [Fact]
    public void MissingCookedAsset_IsRejectedWithoutMutatingWorld(){
        DeterministicPhysicsWorld World=new();
        PhysicsBodyDescription Description=new(
            new Entity(1),1,new Collider { Shape=ColliderShape.ConvexAsset,Asset="missing" },
            null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero);

        Assert.Equal(PhysicsUpdateStatus.InvalidShape,World.Upsert(Description));
        Assert.Equal(0,World.BodyCount);
    }

    [Fact]
    public void InvalidMassAndUnrepresentableBounds_AreRejectedBeforeWorldMutation(){
        DeterministicPhysicsWorld World=new();
        PhysicsBodyDescription TinyMass=PhysicsBodyDescription.CreateDynamicBox(
            new Entity(1),LogicalVector3.Zero,new LogicalVector3(1,1,1),LogicalScalar.FromRaw(1));
        PhysicsBodyDescription OverflowingBounds=PhysicsBodyDescription.CreateStaticBox(
            new Entity(2),new LogicalVector3(LogicalScalar.MaxValue,0,0),new LogicalVector3(1,1,1));

        Assert.Equal(PhysicsUpdateStatus.InvalidMass,World.Upsert(TinyMass));
        Assert.Equal(PhysicsUpdateStatus.NumericOverflow,World.Upsert(OverflowingBounds));
        Assert.Equal(0,World.BodyCount);
    }

    [Fact]
    public void CommandOverflow_RollsBackAndFaultsInsteadOfEscapingTheTick(){
        DeterministicPhysicsWorld World=new();
        Entity Entity=new(1);
        World.Upsert(new PhysicsBodyDescription(
            Entity,1,new Collider(),new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
        LogicalVector3 Maximum=new(LogicalScalar.MaxValue,0,0);
        World.Commands.Impulse(1,7,0,Entity,Maximum);
        World.Commands.Impulse(1,7,1,Entity,Maximum);

        World.Step(1);

        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.Fault&&Event.Fault==PhysicsFaultType.NumericOverflow);
        Assert.True(World.TryGetBodyState(Entity,out PhysicsBodyState State));
        Assert.Equal(LogicalVector3.Zero,State.Position);
        Assert.Equal(PhysicsVelocity.Zero,State.Velocity);
        Assert.True(State.IsSleeping);
    }

    [Fact]
    public void AngularVelocity_IntegratesCanonicalLogicalQuaternion(){
        DeterministicPhysicsWorld World=new();
        Entity Entity=new(1);
        World.Upsert(new PhysicsBodyDescription(
            Entity,1,new Collider(),new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            LogicalVector3.Zero,LogicalQuaternion.Identity,
            new PhysicsVelocity { Angular=new LogicalVector3(0,LogicalScalar.One,0) }));

        World.Step(1);

        Assert.True(World.TryGetBodyState(Entity,out PhysicsBodyState State));
        Assert.NotEqual(LogicalQuaternion.Identity,State.Rotation);
        Assert.True(State.Rotation.TryNormalize(out LogicalQuaternion Normalized));
        Assert.Equal(Normalized,State.Rotation);
    }

    [Fact]
    public void Solver_IsIndependentOfBodyInsertionOrder(){
        PhysicsBodyDescription Ground=PhysicsBodyDescription.CreateStaticBox(
            new Entity(3),LogicalVector3.Zero,new LogicalVector3(5,0.5f,5));
        PhysicsBodyDescription FirstBody=PhysicsBodyDescription.CreateDynamicBox(
            new Entity(1),new LogicalVector3(-0.25f,2,0),new LogicalVector3(0.5f,0.5f,0.5f),LogicalScalar.One);
        PhysicsBodyDescription SecondBody=PhysicsBodyDescription.CreateDynamicBox(
            new Entity(2),new LogicalVector3(0.25f,3,0),new LogicalVector3(0.5f,0.5f,0.5f),LogicalScalar.One);
        DeterministicPhysicsWorld First=new();
        DeterministicPhysicsWorld Second=new();
        foreach(PhysicsBodyDescription Body in new[]{Ground,FirstBody,SecondBody}){ Assert.Equal(PhysicsUpdateStatus.Success,First.Upsert(Body)); }
        foreach(PhysicsBodyDescription Body in new[]{SecondBody,Ground,FirstBody}){ Assert.Equal(PhysicsUpdateStatus.Success,Second.Upsert(Body)); }

        for(ulong Tick=1;Tick<=180;Tick++){ First.Step(Tick);Second.Step(Tick); }

        Assert.Equal(First.ComputeStateHash(),Second.ComputeStateHash());
        Assert.Equal(First.Events,Second.Events);
    }

    [Fact]
    public void RestingContact_HasNoSteadyStateManagedAllocation(){
        DeterministicPhysicsWorld World=new();
        World.Upsert(PhysicsBodyDescription.CreateStaticBox(
            new Entity(2),LogicalVector3.Zero,new LogicalVector3(5,0.5f,5)));
        World.Upsert(PhysicsBodyDescription.CreateDynamicBox(
            new Entity(1),new LogicalVector3(0,1,0),new LogicalVector3(0.5f,0.5f,0.5f),LogicalScalar.One));
        for(ulong Tick=1;Tick<=180;Tick++){ World.Step(Tick); }

        long Before=GC.GetAllocatedBytesForCurrentThread();
        for(ulong Tick=181;Tick<=280;Tick++){ World.Step(Tick); }
        long Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;

        Assert.Equal(0,Allocated);
    }

    [Fact]
    public void EmptyAndContactFreeTicks_HaveNoSteadyStateManagedAllocation(){
        DeterministicPhysicsWorld Empty=new();
        for(ulong Tick=1;Tick<=180;Tick++){ Empty.Step(Tick); }
        long EmptyBefore=GC.GetAllocatedBytesForCurrentThread();
        for(ulong Tick=181;Tick<=280;Tick++){ Empty.Step(Tick); }
        long EmptyAllocated=GC.GetAllocatedBytesForCurrentThread()-EmptyBefore;

        DeterministicPhysicsWorld ContactFree=new();
        Assert.Equal(PhysicsUpdateStatus.Success,ContactFree.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider(),new RigidBody {
                MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero,
            },LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        for(ulong Tick=1;Tick<=180;Tick++){ ContactFree.Step(Tick); }
        long BodyBefore=GC.GetAllocatedBytesForCurrentThread();
        for(ulong Tick=181;Tick<=280;Tick++){ ContactFree.Step(Tick); }
        long BodyAllocated=GC.GetAllocatedBytesForCurrentThread()-BodyBefore;

        Assert.Equal(0,EmptyAllocated);
        Assert.Equal(0,BodyAllocated);
    }

    [Fact]
    public void SphereBoxContact_HasNoSteadyStateManagedAllocation(){
        DeterministicPhysicsWorld World=new();
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(PhysicsBodyDescription.CreateStaticBox(
            new Entity(2),LogicalVector3.Zero,new LogicalVector3(5,0.5f,5))));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.Sphere,Radius=LogicalScalar.FromSingle(0.5f) },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One },
            new LogicalVector3(0,1,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        for(ulong Tick=1;Tick<=180;Tick++){ World.Step(Tick); }

        long Before=GC.GetAllocatedBytesForCurrentThread();
        for(ulong Tick=181;Tick<=280;Tick++){ World.Step(Tick); }
        long Allocated=GC.GetAllocatedBytesForCurrentThread()-Before;

        Assert.Equal(0,Allocated);
    }

    [Fact]
    public void ContinuousStaticCcd_PreventsFastSphereFromCrossingThinStaticWall(){
        DeterministicPhysicsWorld World=new();
        Collider Wall=new() { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(2,2,0.05f) };
        Collider Sphere=new() { Shape=ColliderShape.Sphere,Radius=LogicalScalar.FromSingle(0.25f) };
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,Wall,null,new LogicalVector3(0,0,5),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,Sphere,new RigidBody {
                MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One,
                GravityFactor=LogicalScalar.Zero,Ccd=PhysicsCcdMode.ContinuousStatic,
            },LogicalVector3.Zero,LogicalQuaternion.Identity,
            new PhysicsVelocity { Linear=new LogicalVector3(0,0,600) })));

        World.Step(1);

        Assert.True(World.TryGetBodyState(new Entity(2),out PhysicsBodyState State));
        Assert.True(State.Position.F<LogicalScalar.FromSingle(4.75f));
        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.ContactBegin);
    }

    [Fact]
    public void CookedStaticMeshBvh_ProducesContactWithoutBecomingRuntimeTruth(){
        DeterministicPhysicsWorld World=new();
        PhysicsShapeAsset Mesh=PhysicsShapeAsset.CreateStaticTriangleMesh(
            "ground",
            [new LogicalVector3(-2,0,-2),new LogicalVector3(2,0,-2),new LogicalVector3(2,0,2),new LogicalVector3(-2,0,2)],
            [0,2,1,0,3,2]);
        World.Shapes.Add(Mesh);
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="ground" },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,new Collider { Shape=ColliderShape.Sphere,Radius=LogicalScalar.FromSingle(0.5f) },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            new LogicalVector3(0,0.25f,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));

        World.Step(1);

        Assert.Contains(World.Events,Event=>Event.Type==PhysicsEventType.ContactBegin);
        Assert.True(Mesh.TriangleBvhNodeCount>0);
    }

    [Fact]
    public void ColliderWithoutRigidBody_IsAStaticBodyAndCanBeRaycast(){
        DeterministicPhysicsWorld World=new();
        PhysicsBodyDescription Description=PhysicsBodyDescription.CreateStaticBox(
            new Entity(10),new LogicalVector3(0,0,5),new LogicalVector3(1,1,1));

        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(Description));
        List<PhysicsRayHit> Hits=[];
        PhysicsQueryResult Result=World.Raycast(
            new PhysicsRay(LogicalVector3.Zero,LogicalVector3.UnitF,10),
            PhysicsLayerMask.All,Hits);

        Assert.Equal(PhysicsQueryStatus.Success,Result.Status);
        Assert.Equal(1,Result.AddedCount);
        Assert.Equal(new Entity(10),Hits[0].Entity);
        Assert.True(Hits[0].Distance.TryToSingle(out var Distance));
        Assert.Equal(4f,Distance);
    }

    [Fact]
    public void Step_FallsDynamicBoxAndProducesStableStateHash(){
        DeterministicPhysicsWorld First=new();
        DeterministicPhysicsWorld Second=new();
        PhysicsBodyDescription Body=PhysicsBodyDescription.CreateDynamicBox(
            new Entity(1),new LogicalVector3(0,10,0),new LogicalVector3(0.5f,0.5f,0.5f),LogicalScalar.One);
        Assert.Equal(PhysicsUpdateStatus.Success,First.Upsert(Body));
        Assert.Equal(PhysicsUpdateStatus.Success,Second.Upsert(Body));

        for(ulong Tick=0;Tick<120;Tick++){
            First.Step(Tick);
            Second.Step(Tick);
        }

        Assert.True(First.TryGetBodyState(new Entity(1),out PhysicsBodyState State));
        Assert.True(State.Position.U<LogicalScalar.FromInt64(10));
        Assert.Equal(First.ComputeStateHash(),Second.ComputeStateHash());
        Assert.Equal(First.Events,Second.Events);
    }

    [Fact]
    public void DynamicTriangleMesh_IsRejectedWithoutMutatingWorld(){
        DeterministicPhysicsWorld World=new();
        PhysicsBodyDescription Invalid=new(
            new Entity(3),3,
            new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="mesh.cgecollider" },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One },
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero);

        Assert.Equal(PhysicsUpdateStatus.InvalidShape,World.Upsert(Invalid));
        Assert.Equal(0,World.BodyCount);
    }

    [Fact]
    public void PhysicsSynchronization_RejectsBodiesOutsideTheSelectedDomainFrame(){
        using EngineHost Host=new();
        var Domain=new ReferenceFrameId(StableId.FromName(ReferenceFrameId.Root.Value,"physics-domain"));
        Host.ReferenceFrames.Add(Domain,ReferenceFrameId.Root,FramePose.Identity);
        Assert.True(Host.ReferenceFrames.TryCommit(1,out _,out var Error),Error.Message);
        Host.SetPhysicsFrame(Domain);
        CSharpGameEngine.Entities.World EcsWorld=new();
        Transform Transform=new() { Frame=ReferenceFrameId.Root };
        Collider Collider=new();
        Entity Entity=EcsWorld.Create(Transform,Collider);
        State State=new(EcsWorld);
        PhysicsBodySync System=new();

        Assert.True(System.Update(Entity,ref Transform,ref Collider,Host,ref State));

        Assert.True(Host.TryGetPhysicsSyncFailure(Entity,out var Fault));
        Assert.Equal(PhysicsFaultType.ReferenceFrameMismatch,Fault);
        Assert.False(Host.PhysicsWorld.TryGetBodyState(Entity,out _));
    }

    [Fact]
    public void EngineHost_KeepsPhysicsSolverStateIsolatedPerReferenceFrame(){
        using EngineHost Host=new();
        var FirstFrame=new ReferenceFrameId(StableId.FromName(ReferenceFrameId.Root.Value,"physics-domain-a"));
        var SecondFrame=new ReferenceFrameId(StableId.FromName(ReferenceFrameId.Root.Value,"physics-domain-b"));
        Host.ReferenceFrames.Add(FirstFrame,ReferenceFrameId.Root,FramePose.Identity);
        Host.ReferenceFrames.Add(SecondFrame,ReferenceFrameId.Root,FramePose.Identity);
        Assert.True(Host.ReferenceFrames.TryCommit(1,out _,out var Error),Error.Message);

        Host.SetPhysicsFrame(FirstFrame);
        CSharpGameEngine.Entities.World EcsWorld=new();
        Transform Transform=new() { Frame=FirstFrame };
        Collider Collider=new();
        Entity Entity=EcsWorld.Create(Transform,Collider);
        State State=new(EcsWorld);
        PhysicsSyncBegin Begin=new();
        PhysicsBodySync Sync=new();
        Assert.True(Begin.Update(Host,ref State));
        Assert.True(Sync.Update(Entity,ref Transform,ref Collider,Host,ref State));
        IReadOnlyPhysicsWorld FirstWorld=Host.PhysicsWorld;

        Host.SetPhysicsFrame(SecondFrame);
        Assert.False(Host.PhysicsWorld.TryGetBodyState(Entity,out _));
        Assert.Equal(0,Host.PhysicsWorld.BodyCount);

        Host.SetPhysicsFrame(FirstFrame);
        Assert.Same(FirstWorld,Host.PhysicsWorld);
        Assert.True(Host.PhysicsWorld.TryGetBodyState(Entity,out _));
    }
}
