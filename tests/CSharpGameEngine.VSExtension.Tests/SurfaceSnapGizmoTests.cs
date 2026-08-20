using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SurfaceSnapGizmoTests {
    [Fact]
    public void GetArmedPlacement_UsesPivotAndCameraForward(){
        var Frame=MoveGizmoFrame.Create(
                                        new Vector3(1f,2f,3f),Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        (var Position,var Normal)=SurfaceSnapGizmo.GetArmedPlacement(Frame,new Vector3(0f,0f,1f));

        Assert.Equal(Frame.Pivot,Position);
        Assert.Equal(1f,Normal.Z,4);
    }

    [Fact]
    public void TryHit_RayThroughPivotAlongCameraForward_ReturnsSurfaceSnapPlane(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Hit=SurfaceSnapGizmo.TryHit(
                                       Frame,CameraForward:Vector3.UnitZ,new Vector3(0f,0f,-10f),Vector3.UnitZ,out var RayDistance);

        Assert.Equal(GizmoHandleId.SurfaceSnapPlane,Hit);
        Assert.Equal(10f,RayDistance,4);
    }

    [Fact]
    public void TryHit_RayOutsideQuadHalfExtent_ReturnsNull(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Hit=SurfaceSnapGizmo.TryHit(
                                       Frame,CameraForward:Vector3.UnitZ,new Vector3(5f,5f,-10f),Vector3.UnitZ,out _);

        Assert.Null(Hit);
    }

    [Fact]
    public void TryHitTransform_ArmedRayAtPivot_ReturnsSurfaceSnapPlaneInsteadOfScaleUniform(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Armed=TransformGizmo.TryHitTransform(
                                                Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
                                                CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY,
                                                SurfaceSnapArmed:true);
        var Unarmed=TransformGizmo.TryHitTransform(
                                                  Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
                                                  CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY,
                                                  SurfaceSnapArmed:false);

        Assert.Equal(GizmoHandleId.SurfaceSnapPlane,Armed);
        Assert.Equal(GizmoHandleId.ScaleUniform,Unarmed);
    }

    [Fact]
    public void TryHitTransform_ArmedButSurfaceSnapPlaneDisabled_FallsBackToScaleUniform(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        HashSet<GizmoHandleId>Disabled=[GizmoHandleId.SurfaceSnapPlane];

        var Hit=TransformGizmo.TryHitTransform(
                                              Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
                                              CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY,
                                              SurfaceSnapArmed:true,Disabled:Disabled);

        Assert.Equal(GizmoHandleId.ScaleUniform,Hit);
    }

    [Fact]
    public void BuildOverlay_OutlineColorIsFillColorInverted(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Overlay=SurfaceSnapGizmo.BuildOverlay(
                                                 Frame,Position:Vector3.Zero,Normal:Vector3.UnitZ,Disabled:false);

        var Fill=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle);
        var Outline=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);
        var FillColor=Fill.Vertices[0].Color;
        var OutlineColor=Outline.Vertices[0].Color;

        Assert.Equal(1f-FillColor.X,OutlineColor.X,4);
        Assert.Equal(1f-FillColor.Y,OutlineColor.Y,4);
        Assert.Equal(1f-FillColor.Z,OutlineColor.Z,4);
    }

    [Fact]
    public void BuildOverlay_Disabled_FadesFillAlpha(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Enabled=SurfaceSnapGizmo.BuildOverlay(
                                                 Frame,Position:Vector3.Zero,Normal:Vector3.UnitZ,Disabled:false);
        var Disabled=SurfaceSnapGizmo.BuildOverlay(
                                                  Frame,Position:Vector3.Zero,Normal:Vector3.UnitZ,Disabled:true);

        var EnabledAlpha=Enabled.First(Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle).Vertices[0].Color.W;
        var DisabledAlpha=Disabled.First(Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle).Vertices[0].Color.W;

        Assert.True(DisabledAlpha<EnabledAlpha);
    }
}
