using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0067：StaticMeshColliderは描画三角形を、完全なワールド変換をベイクしてCookする。</summary>
public sealed class StaticMeshCollisionTests {
    private const string BoxAsset="Assets/Primitives/Box.glb";
    private static LogicalAffineTransform Placement(float Y,float ScaleXz){
        Assert.True(LogicalAffineTransform.TryCreateTrs(new LogicalVector3(0,Y,0),LogicalQuaternion.Identity,new LogicalVector3(ScaleXz,1,ScaleXz),out var Result));
        return Result;
    }
    [Fact]
    public void TryEnsure_BakesScaleAndTranslationAndReusesAPlacement(){
        DeterministicPhysicsWorld World=new();
        StaticMeshCollisionCooker Cooker=new();
        Assert.True(Cooker.TryEnsure(World.Shapes,BoxAsset,Placement(-0.5f,4f),out var Id,out var Error),Error);
        Assert.True(World.Shapes.TryGet(Id,out var Asset));
        Assert.Equal(PhysicsShapeAssetKind.StaticTriangleMesh,Asset.Kind);
        Assert.Equal(LogicalScalar.FromSingle(-2f),Asset.Bounds.Min.R);
        Assert.Equal(LogicalScalar.FromSingle(2f),Asset.Bounds.Max.F);
        Assert.Equal(LogicalScalar.Zero,Asset.Bounds.Max.U);
        Assert.Equal(LogicalScalar.FromSingle(-1f),Asset.Bounds.Min.U);
        Assert.True(Cooker.TryEnsure(World.Shapes,BoxAsset,Placement(-0.5f,4f),out var Again,out _));
        Assert.Equal(Id,Again);
        Assert.True(Cooker.TryEnsure(World.Shapes,BoxAsset,Placement(-0.5f,2f),out var Other,out _));
        Assert.NotEqual(Id,Other);
    }
    [Fact]
    public void CookedBox_IsAFloorForCapsuleContacts(){
        DeterministicPhysicsWorld World=new();
        StaticMeshCollisionCooker Cooker=new();
        Assert.True(Cooker.TryEnsure(World.Shapes,BoxAsset,Placement(-0.5f,4f),out var Id,out var Error),Error);
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset=Id },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Collider Capsule=new() { Shape=ColliderShape.Capsule,Radius=LogicalScalar.FromSingle(0.35f),HalfHeight=LogicalScalar.FromSingle(0.55f) };
        List<PhysicsShapeContact> Contacts=[];
        World.CollectContacts(Capsule,new LogicalVector3(0.5f,0.8f,0.5f),LogicalQuaternion.Identity,PhysicsLayerMask.All,Contacts);
        Assert.NotEmpty(Contacts);
        Assert.All(Contacts,Contact=>Assert.True(Contact.Normal.U<LogicalScalar.FromSingle(-0.99f),$"normal {Contact.Normal}"));
    }
    [Fact]
    public void TryEnsure_ReportsAMissingAsset(){
        Assert.False(new StaticMeshCollisionCooker().TryEnsure(new DeterministicPhysicsWorld().Shapes,"Assets/Missing.glb",LogicalAffineTransform.Identity,out _,out var Error));
        Assert.Contains("Missing.glb",Error,StringComparison.Ordinal);
    }
}
