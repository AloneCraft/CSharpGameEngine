using System;
using System.Collections.Generic;
using System.Numerics;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SpatialDebugOverlayTests {
    [Fact]
    public void Build_AlwaysIncludesRootAndCapsAt512WireCubes(){
        List<SceneSpatialDebugNode> Nodes=[new SceneSpatialDebugNode(new Vector3(-1,-1,0),new Vector3(1,1,1),0,10,true)];
        for(int Index=0;Index<600;Index++){
            float Offset=(Index%20)*0.01f;
            Nodes.Add(new SceneSpatialDebugNode(new Vector3(-0.9f+Offset,-0.9f,0.1f),new Vector3(-0.8f+Offset,-0.8f,0.2f),1,1,false));
        }
        SceneSpatialDebugSnapshot Snapshot=new(Guid.NewGuid(),1,false,Nodes,0);
        IReadOnlyList<GizmoOverlayPrimitive> Overlay=SpatialDebugOverlay.Build(Snapshot,Matrix4x4.Identity);
        Assert.Equal(512,Overlay.Count);
        Assert.All(Overlay,Primitive=>{
            Assert.Equal(GizmoPrimitiveKind.Line,Primitive.Kind);
            Assert.Equal(24,Primitive.Vertices.Count);
        });
    }
}