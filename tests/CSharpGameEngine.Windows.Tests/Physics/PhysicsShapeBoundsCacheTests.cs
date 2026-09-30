using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// Static triangle meshes never move, so re-synchronizing and stepping them must not rescan their
/// vertices: the ThirdPerson level spent 95% of each frame recomputing those bounds (about 1 fps).
/// </summary>
public sealed class PhysicsShapeBoundsCacheTests {
    private static PhysicsShapeAsset Grid(string Id,int Cells){
        List<LogicalVector3>Vertices=[];
        List<int>Indices=[];
        for(var Z=0;Z<=Cells;Z++){
            for(var X=0;X<=Cells;X++){
                Vertices.Add(new LogicalVector3(X-Cells/2,(X*7+Z*3)%5*0.01f,Z-Cells/2));
            }
        }
        for(var Z=0;Z<Cells;Z++){
            for(var X=0;X<Cells;X++){
                var A=Z*(Cells+1)+X;
                Indices.AddRange([A,A+Cells+1,A+1,A+1,A+Cells+1,A+Cells+2]);
            }
        }
        return PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices);
    }
    private static PhysicsBodyDescription MeshBody(string Id)=>new(
        new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
        LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero);
    [Fact]
    public void SteadyStateSynchronizeStepAndQueries_DoNotRescanStaticMeshVertices(){
        DeterministicPhysicsWorld World=new();
        var Mesh=Grid("grid",40);
        World.Shapes.Add(Mesh);
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(MeshBody("grid")));
        var Capsule=new Collider { Shape=ColliderShape.Capsule,Radius=0.35f,HalfHeight=0.55f };
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(2),2,new Collider { Shape=ColliderShape.Sphere,Radius=0.5f },
            new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One },
            new LogicalVector3(0,2,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        World.Step(1);
        List<PhysicsShapeContact>Contacts=[];
        Mesh.SupportScanCount=0;
        for(ulong Tick=2;Tick<10;Tick++){
            Assert.Equal(PhysicsUpdateStatus.Success,World.Synchronize(MeshBody("grid")));
            World.Step(Tick);
            Contacts.Clear();
            World.CollectContacts(Capsule,new LogicalVector3(3,0.9f,3),LogicalQuaternion.Identity,PhysicsLayerMask.All,Contacts);
        }
        Assert.Equal(0,Mesh.SupportScanCount);
    }
    [Fact]
    public void CachedBounds_EqualTheSupportComputedBoundsAcrossPosesAndCentres(){
        PhysicsNarrowPhase NarrowPhase=new();
        var Mesh=Grid("grid",6);
        var Convex=PhysicsShapeAsset.CreateConvex("hull",[new(-1,0,-0.5f),new(1.5f,0.2f,0),new(0,2,0.3f),new(0.1f,-0.7f,1.2f)]);
        LogicalQuaternion[]Rotations=[
            LogicalQuaternion.Identity,
            LogicalQuaternion.FromEulerRuf(new LogicalVector3(0.3f,1.1f,-0.4f)),
            LogicalQuaternion.FromEulerRuf(new LogicalVector3(-0.9f,0.2f,2.3f)),
            LogicalQuaternion.Identity];
        LogicalVector3[]Positions=[LogicalVector3.Zero,new(12.25f,-3.5f,7),new(-1000.125f,40,0.5f)];
        foreach(var (Kind,Asset) in new[]{(ColliderShape.StaticTriangleMesh,Mesh),(ColliderShape.ConvexAsset,Convex)}){
            foreach(var Center in new[]{LogicalVector3.Zero,new LogicalVector3(0.25f,-0.5f,1)}){
                foreach(var Rotation in Rotations){
                    foreach(var Position in Positions){
                        var Shape=new PhysicsCollisionShape(new Collider { Shape=Kind,Asset=Asset.Id,Center=Center },Position,Rotation,Asset);
                        Assert.Equal(PhysicsNarrowPhase.CalculateBoundsUncached(Shape),NarrowPhase.CalculateBounds(Shape));
                    }
                }
            }
        }
    }
}
