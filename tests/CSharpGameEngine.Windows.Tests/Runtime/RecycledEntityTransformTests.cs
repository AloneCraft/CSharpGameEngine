using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// ADR-0078/M0105：再利用されたスロットに作られた Entity の Generation は2以上です。固定小数点の階層解決でも、その Transform と親を見つけられなければなりません。これが、すべての描画可能オブジェクト、ライト、リスナー、コライダーの配置に使われます。
/// </summary>
public sealed class RecycledEntityTransformTests {
    private static Entity Recycle(ref EcsWorld World){
        var Old=World.Create(new Transform(),new PrimitiveMesh());
        World.Destroy(Old);
        return Old;
    }
    [Fact]
    public void AnEntityInARecycledSlotResolvesItsOwnTransform(){
        using var Host=new EngineHost();
        ref var World=ref Host.World;
        var Old=Recycle(ref World);
        var Entity=World.Create(new Transform { Position=new LogicalVector3(1f,2f,3f) },new PrimitiveMesh());
        Assert.Equal(Old.Index,Entity.Index);
        Assert.True(Entity.Generation>1,"the slot was not recycled");
        var Status=RuntimeWorldTransform.TryResolveWorld(Entity,World,out var Resolved);
        Assert.Equal(FixedTransformResolveStatus.Success,Status);
        Assert.Equal(new LogicalVector3(1f,2f,3f),new LogicalVector3(Resolved.M41,Resolved.M42,Resolved.M43));
    }
    [Fact]
    public void AParentInARecycledSlotComposesWithItsChild(){
        using var Host=new EngineHost();
        ref var World=ref Host.World;
        Recycle(ref World);
        var Parent=World.Create(new Transform { Position=new LogicalVector3(10f,0f,0f) },new PrimitiveMesh());
        Assert.True(Parent.Generation>1,"the slot was not recycled");
        var Child=World.Create(new Transform { Position=new LogicalVector3(0f,5f,0f) },new PrimitiveMesh(),new SpatialBounds(),new Parent { Value=Parent });
        var Status=RuntimeWorldTransform.TryResolveWorld(Child,World,out var Resolved);
        Assert.Equal(FixedTransformResolveStatus.Success,Status);
        Assert.Equal(new LogicalVector3(10f,5f,0f),new LogicalVector3(Resolved.M41,Resolved.M42,Resolved.M43));
    }
    [Fact]
    public void AStaleHandleToARecycledSlotIsNotResolved(){
        using var Host=new EngineHost();
        ref var World=ref Host.World;
        var Old=Recycle(ref World);
        World.Create(new Transform(),new PrimitiveMesh());
        var Status=RuntimeWorldTransform.TryResolveWorld(Old,World,out _);
        Assert.Equal(FixedTransformResolveStatus.MissingTransform,Status);
    }
}
