using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0077: 光線は静的三角形メッシュの外接ボックスではなく、三角形そのものに当たる(坂の上に立つ車や、クリックした地面のために)。</summary>
public sealed class PhysicsMeshRaycastTests {
    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static DeterministicPhysicsWorld WorldWith(string Id,LogicalVector3[]Vertices,int[]Indices,LogicalVector3? Position=null,LogicalQuaternion? Rotation=null){
        var World=new DeterministicPhysicsWorld();
        World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
            Position??LogicalVector3.Zero,Rotation??LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        return World;
    }
    /// <summary>z=20から40へ、高さ0から3へ上がる坂(幅は±20)。</summary>
    private static DeterministicPhysicsWorld Ramp(LogicalVector3? Position=null,LogicalQuaternion? Rotation=null)=>
        WorldWith("ramp",[new(-20,0,20),new(20,0,20),new(20,3,40),new(-20,3,40)],[0,1,2,0,2,3],Position,Rotation);
    private static List<PhysicsRayHit> Cast(IReadOnlyPhysicsWorld World,LogicalVector3 Origin,LogicalVector3 Direction,float MaxDistance){
        var Hits=new List<PhysicsRayHit>();
        Assert.Equal(PhysicsQueryStatus.Success,World.Raycast(new PhysicsRay(Origin,Direction,LogicalScalar.FromSingle(MaxDistance)),PhysicsLayerMask.All,Hits).Status);
        return Hits;
    }
    [Fact]
    public void ADownwardRayHitsTheSlopeAtItsSurfaceHeight(){
        var Hits=Cast(Ramp(),new LogicalVector3(0,10,30),-LogicalVector3.UnitU,20);
        var Hit=Assert.Single(Hits);
        // z=30は坂の真ん中: 高さ1.5 m。
        Assert.InRange(ToSingle(Hit.Point.U),1.499f,1.501f);
        Assert.InRange(ToSingle(Hit.Distance),8.499f,8.501f);
        // 面の法線は上を向く(光線に向いた側)。傾きは約8.5度なので、上向き成分が約0.989。
        Assert.InRange(ToSingle(Hit.Normal.U),0.98f,0.995f);
    }
    [Fact]
    public void ARayThroughTheBoundingBoxButAboveTheSurfaceMisses(){
        // 外接ボックスは高さ0から3。z=22の坂の高さは0.3 mなので、高さ2 mの水平な光線は坂の上を通り抜ける。
        var Hits=Cast(Ramp(),new LogicalVector3(0,2,10),LogicalVector3.UnitF,12);
        Assert.Empty(Hits);
    }
    [Fact]
    public void AHorizontalRayHitsTheSlopeWhereItRisesToTheRayHeight(){
        var Hits=Cast(Ramp(),new LogicalVector3(0,1.5f,10),LogicalVector3.UnitF,30);
        var Hit=Assert.Single(Hits);
        Assert.InRange(ToSingle(Hit.Point.F),29.99f,30.01f);
        Assert.InRange(ToSingle(Hit.Distance),19.99f,20.01f);
    }
    [Fact]
    public void ARayFromBelowHitsTheSameTriangleAndTheNormalFacesTheRay(){
        var Hits=Cast(Ramp(),new LogicalVector3(0,-5,30),LogicalVector3.UnitU,20);
        var Hit=Assert.Single(Hits);
        Assert.InRange(ToSingle(Hit.Point.U),1.499f,1.501f);
        Assert.True(ToSingle(Hit.Normal.U)<-0.98f,$"normal {ToSingle(Hit.Normal.U)}");
    }
    [Fact]
    public void ARayBesideTheMeshMisses(){
        Assert.Empty(Cast(Ramp(),new LogicalVector3(25,10,30),-LogicalVector3.UnitU,20));
    }
    [Fact]
    public void ARayShorterThanTheDistanceToTheSurfaceMisses(){
        Assert.Empty(Cast(Ramp(),new LogicalVector3(0,10,30),-LogicalVector3.UnitU,8));
    }
    [Fact]
    public void ThePlacementOfTheBodyIsApplied(){
        // 坂を右(+R)へ100 m、上へ10 m置く。元の場所の真上からの光線は当たらず、置いた場所のz=30では高さ11.5 mに当たる。
        var World=Ramp(new LogicalVector3(100,10,0));
        Assert.Empty(Cast(World,new LogicalVector3(0,50,30),-LogicalVector3.UnitU,60));
        var Hit=Assert.Single(Cast(World,new LogicalVector3(100,50,30),-LogicalVector3.UnitU,60));
        Assert.InRange(ToSingle(Hit.Point.U),11.499f,11.501f);
    }
    [Fact]
    public void TheRotationOfTheBodyIsApplied(){
        // 坂を上向き(+U)の軸のまわりに90度回す(+Fが+Rへ向く)。z方向に伸びていた坂がr方向へ伸びる。
        var Quarter=LogicalQuaternion.FromEulerRuf(new LogicalVector3(0,LogicalMath.HalfPi,0));
        var World=Ramp(null,Quarter);
        var Hit=Assert.Single(Cast(World,new LogicalVector3(30,10,0),-LogicalVector3.UnitU,20));
        Assert.InRange(ToSingle(Hit.Point.U),1.49f,1.51f);
        Assert.Empty(Cast(World,new LogicalVector3(0,10,30),-LogicalVector3.UnitU,20));
    }
    [Fact]
    public void ABoxBodyIsStillHitAsItsBounds(){
        var World=new DeterministicPhysicsWorld();
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(PhysicsBodyDescription.CreateStaticBox(new Entity(2),new LogicalVector3(0,1,10),new LogicalVector3(1,1,1))));
        var Hit=Assert.Single(Cast(World,new LogicalVector3(0,1,0),LogicalVector3.UnitF,20));
        Assert.InRange(ToSingle(Hit.Distance),8.99f,9.01f);
    }
}
