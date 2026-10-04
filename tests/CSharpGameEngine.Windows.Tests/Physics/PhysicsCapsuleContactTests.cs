using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0067: closed-form capsule-triangle contacts and the all-contacts query used by character movement.</summary>
public sealed class PhysicsCapsuleContactTests {
    private static readonly LogicalScalar Tolerance=LogicalScalar.FromSingle(0.002f);
    private static readonly Collider Capsule=new() { Shape=ColliderShape.Capsule,Radius=LogicalScalar.FromSingle(0.35f),HalfHeight=LogicalScalar.FromSingle(0.55f) };
    private static DeterministicPhysicsWorld CreateWorld(params (string Id,LogicalVector3[] Vertices,int[] Indices)[]Meshes){
        DeterministicPhysicsWorld World=new();
        ulong StableId=1;
        foreach(var (Id,Vertices,Indices) in Meshes){
            World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices));
            Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
                new Entity((int)StableId),StableId,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
                LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
            StableId++;
        }
        return World;
    }
    private static (string,LogicalVector3[],int[]) Floor()=>("floor",[new(-5,0,-5),new(5,0,-5),new(5,0,5),new(-5,0,5)],[0,2,1,0,3,2]);
    private static (string,LogicalVector3[],int[]) WallAtX(float X)=>("wall",[new(X,-1,-5),new(X,-1,5),new(X,4,5),new(X,4,-5)],[0,1,2,0,2,3]);
    private static LogicalScalar Abs(LogicalScalar Value)=>Value<LogicalScalar.Zero?-Value:Value;
    private static void AssertNear(LogicalScalar Expected,LogicalScalar Actual,string Label)=>
        Assert.True(Abs(Expected-Actual)<=Tolerance,$"{Label}: expected {Expected}, actual {Actual}");
    [Fact]
    public void CollectContacts_CapsuleSunkIntoAFloorReportsItsDepthAndADownwardNormal(){
        var World=CreateWorld(Floor());
        List<PhysicsShapeContact> Contacts=[];
        // Bottom of the capsule is 0.1 m below the floor: centre height 0.55 + 0.35 - 0.1.
        Assert.Equal(PhysicsQueryStatus.Success,World.CollectContacts(Capsule,new LogicalVector3(0.3f,0.8f,-0.2f),LogicalQuaternion.Identity,PhysicsLayerMask.All,Contacts).Status);
        Assert.NotEmpty(Contacts);
        foreach(var Contact in Contacts){
            AssertNear(LogicalScalar.FromSingle(0.1005f),Contact.Penetration,"penetration");
            AssertNear(-LogicalScalar.One,Contact.Normal.U,"normal.U");
        }
    }
    [Fact]
    public void CollectContacts_ReportsTheFloorAndTheWallTogether(){
        var World=CreateWorld(Floor(),WallAtX(1f));
        List<PhysicsShapeContact> Contacts=[];
        World.CollectContacts(Capsule,new LogicalVector3(0.7f,0.85f,0f),LogicalQuaternion.Identity,PhysicsLayerMask.All,Contacts);
        Assert.Contains(Contacts,Contact=>Contact.StableId==1&&Contact.Normal.U<LogicalScalar.FromSingle(-0.99f));
        // Both wall triangles touch the capsule; the one whose face region contains the segment is the deepest.
        var WallContacts=Contacts.Where(Contact=>Contact.StableId==2).ToArray();
        Assert.All(WallContacts,Contact=>Assert.True(Contact.Normal.R>LogicalScalar.FromSingle(0.9f),$"wall normal {Contact.Normal}"));
        var Wall=WallContacts.MaxBy(Contact=>Contact.Penetration);
        AssertNear(LogicalScalar.One,Wall.Normal.R,"wall normal.R");
        AssertNear(LogicalScalar.FromSingle(0.0505f),Wall.Penetration,"wall penetration");
    }
    [Fact]
    public void CollectContacts_ASegmentThroughTheFloorIsPushedTowardItsCentre(){
        var World=CreateWorld(Floor());
        List<PhysicsShapeContact> Contacts=[];
        // Centre 0.3 m above the floor: the lower segment end is 0.25 m below it.
        World.CollectContacts(Capsule,new LogicalVector3(0,0.3f,0),LogicalQuaternion.Identity,PhysicsLayerMask.All,Contacts);
        Assert.NotEmpty(Contacts);
        Assert.All(Contacts,Contact=>Assert.True(Contact.Normal.U<LogicalScalar.Zero,$"normal {Contact.Normal}"));
        // Clearing the floor needs radius 0.35 plus the 0.25 m the segment end is below it.
        AssertNear(LogicalScalar.FromSingle(0.6005f),Contacts.Max(Contact=>Contact.Penetration),"penetration");
    }
    [Fact]
    public void CollectContacts_SeparatedCapsuleHasNoContacts(){
        var World=CreateWorld(Floor(),WallAtX(1f));
        List<PhysicsShapeContact> Contacts=[];
        World.CollectContacts(Capsule,new LogicalVector3(0,0.95f,0),LogicalQuaternion.Identity,PhysicsLayerMask.All,Contacts);
        Assert.Empty(Contacts);
    }
    [Fact]
    public void ClosestSegmentSegment_MatchesTheAnalyticDistanceOfSkewSegments(){
        // Segment along X at y=0 and segment along Z at y=2 cross in projection: distance 2.
        PhysicsNarrowPhase.ClosestSegmentSegment(new LogicalVector3(-1,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0.5f,2,-1),new LogicalVector3(0.5f,2,1),out var First,out var Second);
        AssertNear(LogicalScalar.FromSingle(0.5f),First.R,"first.R");
        AssertNear(LogicalScalar.Zero,Second.F,"second.F");
        AssertNear(LogicalScalar.FromSingle(4f),(Second-First).LengthSquared,"distance squared");
    }
    [Fact]
    public void DynamicCapsule_SettlesOnATriangleMeshFloor(){
        var World=CreateWorld(Floor());
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(10),10,Capsule,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=LogicalScalar.One },
            new LogicalVector3(0,2,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        for(ulong Tick=1;Tick<=240;Tick++){ World.Step(Tick); }
        Assert.True(World.TryGetBodyState(new Entity(10),out var State));
        Assert.True(Abs(State.Position.U-LogicalScalar.FromSingle(0.9f))<=LogicalScalar.FromSingle(0.05f),$"rest height {State.Position.U}");
    }
}
