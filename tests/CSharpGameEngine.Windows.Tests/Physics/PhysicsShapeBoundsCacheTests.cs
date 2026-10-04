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
    public void IdentityRotation_ReturnsTheVectorExactly(){
        var Random=new Random(5489);
        for(var Index=0;Index<200;Index++){
            var Value=new LogicalVector3((float)(Random.NextDouble()*2000-1000),(float)(Random.NextDouble()*2-1),(float)(Random.NextDouble()*1e-3));
            var General=new LogicalQuaternion(LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.Zero,LogicalScalar.One);
            Assert.True(LogicalQuaternion.Identity.TryRotate(Value,out var Rotated));
            Assert.Equal(Value,Rotated);
            // The general q·p·q* product that the shortcut skips gives the same vector.
            Assert.True(LogicalQuaternion.TryMultiply(General,new LogicalQuaternion(Value.R,Value.U,Value.F,LogicalScalar.Zero),out var Product));
            Assert.True(LogicalQuaternion.TryMultiply(Product,General.Conjugate(),out Product));
            Assert.Equal(Value,new LogicalVector3(Product.R,Product.U,Product.F));
        }
    }
    [Fact]
    public void TriangleCulling_KeepsEveryMeshContact(){
        PhysicsNarrowPhase NarrowPhase=new();
        var Mesh=Grid("grid",12);
        var Random=new Random(97);
        LogicalQuaternion[]MeshRotations=[LogicalQuaternion.Identity,LogicalQuaternion.FromEulerRuf(new LogicalVector3(0.2f,0.9f,-0.1f))];
        List<PhysicsContactCandidate>Culled=[];
        List<PhysicsContactCandidate>Exhaustive=[];
        var Touching=0;
        foreach(var MeshRotation in MeshRotations){
            var MeshShape=new PhysicsCollisionShape(new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Mesh.Id },new LogicalVector3(0.5f,-0.25f,0),MeshRotation,Mesh);
            for(var Index=0;Index<300;Index++){
                var Kind=Index%2==0?ColliderShape.Capsule:ColliderShape.Sphere;
                var Query=new PhysicsCollisionShape(
                    new Collider { Shape=Kind,Radius=0.3f+(float)Random.NextDouble()*0.3f,HalfHeight=0.2f+(float)Random.NextDouble()*0.6f },
                    new LogicalVector3((float)(Random.NextDouble()*12-6),(float)(Random.NextDouble()*1.6-0.8),(float)(Random.NextDouble()*12-6)),
                    Index%3==0?LogicalQuaternion.FromEulerRuf(new LogicalVector3((float)Random.NextDouble(),(float)Random.NextDouble(),0)):LogicalQuaternion.Identity,
                    null);
                Culled.Clear();
                Exhaustive.Clear();
                NarrowPhase.CollectMeshContacts(Query,MeshShape,Culled,true);
                NarrowPhase.CollectMeshContacts(Query,MeshShape,Exhaustive,false);
                Assert.Equal(Exhaustive,Culled);
                Touching+=Culled.Count>0?1:0;
            }
        }
        Assert.InRange(Touching,100,600);
    }
    [Fact]
    public void CachedPrimitiveBounds_EqualTheSupportComputedBounds(){
        PhysicsNarrowPhase NarrowPhase=new();
        Collider[]Colliders=[
            new() { Shape=ColliderShape.Capsule,Radius=0.35f,HalfHeight=0.55f },
            new() { Shape=ColliderShape.Sphere,Radius=0.12f,Center=new LogicalVector3(0,0.3f,0) },
            new() { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(1,0.5f,2) }];
        LogicalQuaternion[]Rotations=[LogicalQuaternion.Identity,LogicalQuaternion.FromEulerRuf(new LogicalVector3(0.4f,-1.3f,0.7f))];
        LogicalVector3[]Positions=[LogicalVector3.Zero,new(3.5f,0.95f,-12),new(-250.75f,7,0.125f)];
        for(var Pass=0;Pass<2;Pass++){
            foreach(var Collider in Colliders){
                foreach(var Rotation in Rotations){
                    foreach(var Position in Positions){
                        var Shape=new PhysicsCollisionShape(Collider,Position,Rotation,null);
                        Assert.Equal(PhysicsNarrowPhase.CalculateBoundsUncached(Shape),NarrowPhase.CalculateBounds(Shape));
                    }
                }
            }
        }
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
