using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class PhysicsWorldTests {
    [Fact]
    public void ColliderWithoutRigidBody_IsAStaticBodyAndCanBeRaycast(){
        DeterministicPhysicsWorld World=new();
        PhysicsBodyDescription Description=PhysicsBodyDescription.CreateStaticBox(
            new Entity(10),new PhysicsVector3(0,0,5),new PhysicsVector3(1,1,1));

        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(Description));
        List<PhysicsRayHit> Hits=[];
        PhysicsQueryResult Result=World.Raycast(
            new PhysicsRay(PhysicsVector3.Zero,PhysicsVector3.UnitF,10),
            PhysicsLayerMask.All,Hits);

        Assert.Equal(PhysicsQueryStatus.Success,Result.Status);
        Assert.Equal(1,Result.AddedCount);
        Assert.Equal(new Entity(10),Hits[0].Entity);
        Assert.Equal(4d,Hits[0].Distance.ToDouble());
    }

    [Fact]
    public void Step_FallsDynamicBoxAndProducesStableStateHash(){
        DeterministicPhysicsWorld First=new();
        DeterministicPhysicsWorld Second=new();
        PhysicsBodyDescription Body=PhysicsBodyDescription.CreateDynamicBox(
            new Entity(1),new PhysicsVector3(0,10,0),new PhysicsVector3(0.5,0.5,0.5),PhysicsScalar.One);
        Assert.Equal(PhysicsUpdateStatus.Success,First.Upsert(Body));
        Assert.Equal(PhysicsUpdateStatus.Success,Second.Upsert(Body));

        for(ulong Tick=0;Tick<120;Tick++){
            First.Step(Tick);
            Second.Step(Tick);
        }

        Assert.True(First.TryGetBodyState(new Entity(1),out PhysicsBodyState State));
        Assert.True(State.Position.U<PhysicsScalar.FromInt32(10));
        Assert.Equal(First.ComputeStateHash(),Second.ComputeStateHash());
        Assert.Equal(First.Events,Second.Events);
    }

    [Fact]
    public void DynamicTriangleMesh_IsRejectedWithoutMutatingWorld(){
        DeterministicPhysicsWorld World=new();
        PhysicsBodyDescription Invalid=new(
            new Entity(3),3,
            new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="mesh.cgecollider" },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=PhysicsScalar.One },
            PhysicsVector3.Zero,PhysicsQuaternion.Identity,PhysicsVelocity.Zero);

        Assert.Equal(PhysicsUpdateStatus.InvalidShape,World.Upsert(Invalid));
        Assert.Equal(0,World.BodyCount);
    }
}
