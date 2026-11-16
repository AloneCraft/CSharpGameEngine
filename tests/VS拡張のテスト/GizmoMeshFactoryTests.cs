using System.Linq;
using System.Numerics;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class GizmoMeshFactoryTests {
    [Fact]
    public void Create_ProducesTwentyFourVerticesAndThirtySixIndices(){
        var Mesh=GizmoMeshFactory.Create();

        Assert.Equal(24,Mesh.Positions.Length);
        Assert.Equal(24,Mesh.Normals.Length);
        Assert.Equal(36,Mesh.Indices.Length);
    }

    [Fact]
    public void Create_AllNormalsAreUnitLength(){
        var Mesh=GizmoMeshFactory.Create();

        foreach(var Normal in Mesh.Normals){
            Assert.Equal(1f,Normal.Length(),precision:5);
        }
    }

    [Fact]
    public void Create_NoDegenerateTriangles(){
        var Mesh=GizmoMeshFactory.Create();

        for(var I=0;I<Mesh.Indices.Length;I+=3){
            var A=Mesh.Positions[Mesh.Indices[I]];
            var B=Mesh.Positions[Mesh.Indices[I+1]];
            var C=Mesh.Positions[Mesh.Indices[I+2]];

            var Area=Vector3.Cross(B-A,C-A).Length();
            Assert.True(Area>0.0001f, $"Degenerate triangle at index {I}");
        }
    }

    [Fact]
    public void Create_IsAxisAlignedAndCenteredAtOrigin(){
        var Mesh=GizmoMeshFactory.Create(Size:0.4f);

        Assert.All(Mesh.Positions,P=>Assert.True(System.Math.Abs(P.X)<=0.2001f&&System.Math.Abs(P.Y)<=0.2001f&&System.Math.Abs(P.Z)<=0.2001f));
        Assert.Equal(Vector3.Zero,Mesh.BoundsCenter);
        Assert.True(Mesh.BoundsRadius>0f);
    }

    [Fact]
    public void Create_UsesDefaultTint_DistinctFromSelectionOrange(){
        var Mesh=GizmoMeshFactory.Create();

        Assert.Equal(1f,Mesh.BaseColorFactor.W);
        Assert.True(Mesh.BaseColorFactor.Z>Mesh.BaseColorFactor.X,"Default gizmo tint should read as blue-ish, not orange.");
    }

    [Fact]
    public void Create_NonPositiveSize_Throws(){
        Assert.Throws<System.ArgumentOutOfRangeException>(()=>GizmoMeshFactory.Create(Size:0f));
    }
}
