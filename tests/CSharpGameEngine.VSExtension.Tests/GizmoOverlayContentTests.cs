using System.Numerics;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>The Scene view requests a preview frame only when the rebuilt overlay differs from the drawn one.</summary>
public sealed class GizmoOverlayContentTests {
    private static GizmoOverlayPrimitive Line(float X,Vector4 Color)=>new(GizmoPrimitiveKind.Line,
        [new GizmoOverlayVertex(new Vector3(X,0f,0f),Color),new GizmoOverlayVertex(new Vector3(X,1f,0f),Color)]);
    [Fact]
    public void IdenticallyRebuiltOverlay_IsTheSameContent(){
        GizmoOverlayPrimitive[]Drawn=[Line(0f,Vector4.One),Line(1f,Vector4.UnitX)];
        GizmoOverlayPrimitive[]Rebuilt=[Line(0f,Vector4.One),Line(1f,Vector4.UnitX)];
        Assert.True(GizmoOverlayPrimitive.SameContent(Drawn,Rebuilt));
        Assert.True(GizmoOverlayPrimitive.SameContent([],[]));
    }
    [Fact]
    public void MovedRecolouredReorderedOrMissingPrimitives_AreDifferentContent(){
        GizmoOverlayPrimitive[]Drawn=[Line(0f,Vector4.One),Line(1f,Vector4.UnitX)];
        Assert.False(GizmoOverlayPrimitive.SameContent(Drawn,[Line(0f,Vector4.One),Line(1.001f,Vector4.UnitX)]));
        Assert.False(GizmoOverlayPrimitive.SameContent(Drawn,[Line(0f,Vector4.One),Line(1f,Vector4.UnitY)]));
        Assert.False(GizmoOverlayPrimitive.SameContent(Drawn,[Line(1f,Vector4.UnitX),Line(0f,Vector4.One)]));
        Assert.False(GizmoOverlayPrimitive.SameContent(Drawn,[Line(0f,Vector4.One)]));
    }
}
