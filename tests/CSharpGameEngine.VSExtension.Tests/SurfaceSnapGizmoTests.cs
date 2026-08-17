using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SurfaceSnapGizmoTests {
    [Fact]
    public void GetArmedPlacement_UsesPivotAndCameraForward(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            new Vector3(1f,2f,3f),Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        (Vector3 Position,Vector3 Normal)=SurfaceSnapGizmo.GetArmedPlacement(Frame,new Vector3(0f,0f,1f));

        Assert.Equal(Frame.Pivot,Position);
        Assert.Equal(1f,Normal.Z,4);
    }

    [Fact]
    public void TryHit_RayThroughPivotAlongCameraForward_ReturnsSurfaceSnapPlane(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Hit=SurfaceSnapGizmo.TryHit(
            Frame,CameraForward:Vector3.UnitZ,new Vector3(0f,0f,-10f),Vector3.UnitZ,out float RayDistance);

        Assert.Equal(GizmoHandleId.SurfaceSnapPlane,Hit);
        Assert.Equal(10f,RayDistance,4);
    }

    [Fact]
    public void TryHit_RayOutsideQuadHalfExtent_ReturnsNull(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Hit=SurfaceSnapGizmo.TryHit(
            Frame,CameraForward:Vector3.UnitZ,new Vector3(5f,5f,-10f),Vector3.UnitZ,out _);

        Assert.Null(Hit);
    }

    [Fact]
    public void TryHitTransform_ArmedRayAtPivot_ReturnsSurfaceSnapPlaneInsteadOfScaleUniform(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Armed=TransformGizmo.TryHitTransform(
            Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY,
            SurfaceSnapArmed:true);
        GizmoHandleId?Unarmed=TransformGizmo.TryHitTransform(
            Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY,
            SurfaceSnapArmed:false);

        Assert.Equal(GizmoHandleId.SurfaceSnapPlane,Armed);
        Assert.Equal(GizmoHandleId.ScaleUniform,Unarmed);
    }

    [Fact]
    public void TryHitTransform_ArmedButSurfaceSnapPlaneDisabled_FallsBackToScaleUniform(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        HashSet<GizmoHandleId>Disabled=[GizmoHandleId.SurfaceSnapPlane];

        GizmoHandleId?Hit=TransformGizmo.TryHitTransform(
            Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY,
            SurfaceSnapArmed:true,Disabled:Disabled);

        Assert.Equal(GizmoHandleId.ScaleUniform,Hit);
    }

    [Fact]
    public void BuildOverlay_OutlineColorIsFillColorInverted(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=SurfaceSnapGizmo.BuildOverlay(
            Frame,Position:Vector3.Zero,Normal:Vector3.UnitZ,Disabled:false);

        GizmoOverlayPrimitive Fill=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle);
        GizmoOverlayPrimitive Outline=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);
        Vector4 FillColor=Fill.Vertices[0].Color;
        Vector4 OutlineColor=Outline.Vertices[0].Color;

        Assert.Equal(1f-FillColor.X,OutlineColor.X,4);
        Assert.Equal(1f-FillColor.Y,OutlineColor.Y,4);
        Assert.Equal(1f-FillColor.Z,OutlineColor.Z,4);
    }

    [Fact]
    public void BuildOverlay_Disabled_FadesFillAlpha(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        IReadOnlyList<GizmoOverlayPrimitive>Enabled=SurfaceSnapGizmo.BuildOverlay(
            Frame,Position:Vector3.Zero,Normal:Vector3.UnitZ,Disabled:false);
        IReadOnlyList<GizmoOverlayPrimitive>Disabled=SurfaceSnapGizmo.BuildOverlay(
            Frame,Position:Vector3.Zero,Normal:Vector3.UnitZ,Disabled:true);

        float EnabledAlpha=Enabled.First(Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle).Vertices[0].Color.W;
        float DisabledAlpha=Disabled.First(Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle).Vertices[0].Color.W;

        Assert.True(DisabledAlpha<EnabledAlpha);
    }
}
