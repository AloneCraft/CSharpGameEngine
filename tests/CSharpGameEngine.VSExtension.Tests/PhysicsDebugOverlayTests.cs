using System.Numerics;
using CSharpGameEngine.VSExtension;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PhysicsDebugOverlayTests {
    [Fact]
    public void Build_EmitsTwentyFourBoundsVerticesPerBody(){
        ScenePhysicsDebugSnapshot Snapshot=new(Guid.NewGuid(),42,[Body(1,false)],0);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:false,CenterOfMass:false,Sleep:true));

        Assert.Single(Result);
        Assert.Equal(24,Result[0].Vertices.Count);
    }

    [Fact]
    public void Build_AddsVelocityAndCenterOfMassWithoutChangingBoundsBudget(){
        ScenePhysicsDebugSnapshot Snapshot=new(Guid.NewGuid(),1,[Body(1,false)],0);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:true,CenterOfMass:true,Sleep:true));

        Assert.Equal(3,Result.Count);
        Assert.Equal(32,Result.Sum(static Primitive=>Primitive.Vertices.Count));
    }

    [Fact]
    public void Build_CapsVisibleBodiesDeterministically(){
        List<ScenePhysicsDebugBody> Bodies=[];
        for(int Index=0;Index<PhysicsDebugOverlay.MaximumBodies+4;Index++){ Bodies.Add(Body(Index,false)); }
        ScenePhysicsDebugSnapshot Snapshot=new(Guid.NewGuid(),1,Bodies,0);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:false,CenterOfMass:false,Sleep:true));

        Assert.Equal(PhysicsDebugOverlay.MaximumBodies,Result.Count);
    }

    private static ScenePhysicsDebugBody Body(int Id,bool Sleeping)=>new(
        Id,(ulong)Id,new Vector3(-1),new Vector3(1),Vector3.Zero,Vector3.UnitX,Sleeping,"Dynamic");
}
