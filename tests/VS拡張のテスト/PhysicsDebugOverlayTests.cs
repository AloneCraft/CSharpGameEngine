using System.Numerics;
using CSharpGameEngine.VSExtension;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PhysicsDebugOverlayTests {
    [Fact]
    public void Build_EmitsTwentyFourBoundsVerticesPerBody(){
        ScenePhysicsDebugSnapshot Snapshot=SnapshotWith([Body(1,false)]);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:false,CenterOfMass:false,Sleep:true,Contacts:false,Tree:false,Bonds:false));

        Assert.Single(Result);
        Assert.Equal(24,Result[0].Vertices.Count);
    }

    [Fact]
    public void Build_AddsVelocityAndCenterOfMassWithoutChangingBoundsBudget(){
        ScenePhysicsDebugSnapshot Snapshot=SnapshotWith([Body(1,false)]);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:true,CenterOfMass:true,Sleep:true,Contacts:false,Tree:false,Bonds:false));

        Assert.Equal(3,Result.Count);
        Assert.Equal(32,Result.Sum(static Primitive=>Primitive.Vertices.Count));
    }

    [Fact]
    public void Build_CapsVisibleBodiesDeterministically(){
        List<ScenePhysicsDebugBody> Bodies=[];
        for(int Index=0;Index<PhysicsDebugOverlay.MaximumBodies+4;Index++){ Bodies.Add(Body(Index,false)); }
        ScenePhysicsDebugSnapshot Snapshot=SnapshotWith(Bodies);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:false,CenterOfMass:false,Sleep:true,Contacts:false,Tree:false,Bonds:false));

        Assert.Equal(PhysicsDebugOverlay.MaximumBodies,Result.Count);
    }

    [Fact]
    public void Build_EmitsContactCrossAndNormalBeforeTreeAndBodies(){
        ScenePhysicsDebugSnapshot Snapshot=new(Guid.NewGuid(),1,[Body(1,false)],
            [new ScenePhysicsDebugContact(9,Vector3.Zero,Vector3.UnitY,0.25f)],
            [new ScenePhysicsDebugTreeNode(new Vector3(-2),new Vector3(2),0,false,0)],0,0,0);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:false,CenterOfMass:false,Sleep:true,Contacts:true,Tree:true,Bonds:false));

        Assert.Equal(4,Result.Count);
        Assert.Equal(6,Result[0].Vertices.Count);
        Assert.Equal(2,Result[1].Vertices.Count);
        Assert.Equal(24,Result[2].Vertices.Count);
        Assert.Equal(24,Result[3].Vertices.Count);
    }

    [Fact]
    public void Build_NeverExceedsTotalVertexBudget(){
        List<ScenePhysicsDebugTreeNode> Nodes=[];
        for(int Index=0;Index<PhysicsDebugOverlay.MaximumVertices;Index++){
            Nodes.Add(new ScenePhysicsDebugTreeNode(new Vector3(-1),new Vector3(1),Index%32,false,0));
        }
        ScenePhysicsDebugSnapshot Snapshot=new(Guid.NewGuid(),1,[Body(1,false)],[],Nodes,0,0,0);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,new PhysicsDebugOverlayOptions(Bounds:true,Velocity:true,CenterOfMass:true,Sleep:true,Contacts:true,Tree:true,Bonds:false));

        Assert.True(Result.Sum(static Primitive=>Primitive.Vertices.Count)<=PhysicsDebugOverlay.MaximumVertices);
        Assert.DoesNotContain(Result,static Primitive=>Primitive.Vertices.Count is 2 or 6);
    }

    [Fact]
    public void Build_EmitsArtifactDerivedFractureBondWithinSharedBudget(){
        ScenePhysicsDebugSnapshot Snapshot=SnapshotWith([]);
        ScenePhysicsFractureBond Bond=new(new Vector3(1,2,3),new Vector3(4,5,6),8f);

        IReadOnlyList<GizmoOverlayPrimitive> Result=PhysicsDebugOverlay.Build(
            Snapshot,
            new PhysicsDebugOverlayOptions(Bounds:false,Velocity:false,CenterOfMass:false,Sleep:true,Contacts:false,Tree:false,Bonds:true),
            [Bond]);

        GizmoOverlayPrimitive Primitive=Assert.Single(Result);
        Assert.Equal(2,Primitive.Vertices.Count);
        Assert.Equal(Bond.First,Primitive.Vertices[0].Position);
        Assert.Equal(Bond.Second,Primitive.Vertices[1].Position);
    }

    private static ScenePhysicsDebugSnapshot SnapshotWith(IReadOnlyList<ScenePhysicsDebugBody>Bodies)=>
        new(Guid.NewGuid(),1,Bodies,[],[],0,0,0);

    private static ScenePhysicsDebugBody Body(int Id,bool Sleeping)=>new(
        Id,(ulong)Id,new Vector3(-1),new Vector3(1),Vector3.Zero,Vector3.UnitX,Sleeping,"Dynamic");
}
