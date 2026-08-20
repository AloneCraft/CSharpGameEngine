using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class TransformGizmoTests {
    [Fact]
    public void ComputeWorldScale_Perspective_GrowsLinearlyWithDistance(){
        float Near=TransformGizmo.ComputeWorldScale(
            SceneProjectionMode.Perspective,DistanceToCamera:5f,FovYRadians:1f,OrthographicSize:10f);
        float Far=TransformGizmo.ComputeWorldScale(
            SceneProjectionMode.Perspective,DistanceToCamera:10f,FovYRadians:1f,OrthographicSize:10f);

        Assert.Equal(Near*2f,Far,5);
    }

    [Fact]
    public void ComputeWorldScale_Orthographic_DoesNotDependOnDistance(){
        float Near=TransformGizmo.ComputeWorldScale(
            SceneProjectionMode.Orthographic,DistanceToCamera:5f,FovYRadians:1f,OrthographicSize:7f);
        float Far=TransformGizmo.ComputeWorldScale(
            SceneProjectionMode.Orthographic,DistanceToCamera:100f,FovYRadians:1f,OrthographicSize:7f);

        Assert.Equal(Near,Far);
    }

    [Fact]
    public void AxisDrag_WorldXPreservesTheActualPositiveWorldAxis(){
        var Frame=MoveGizmoFrame.Create(Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Vector3 StartOrigin=new(0f,0f,-10f);
        var StartDirection=Vector3.Normalize(Vector3.Zero-StartOrigin);
        Vector3 Target=new(2f,0f,0f);
        var CurrentDirection=Vector3.Normalize(Target-StartOrigin);

        Assert.True(MoveGizmoDrag.TryBegin(Frame,GizmoHandleId.AxisX,StartOrigin,StartDirection,Vector3.UnitZ,out MoveGizmoDrag?Drag));
        Assert.True(Drag!.TryUpdate(StartOrigin,CurrentDirection,out Vector3 Delta));

        Assert.Equal(2f,Delta.X,4);
        Assert.Equal(0f,Delta.Y,4);
        Assert.Equal(0f,Delta.Z,4);
    }

    [Fact]
    public void PlaneDrag_XYConstrainsDeltaToThePlane(){
        var Frame=MoveGizmoFrame.Create(Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Vector3 Origin=new(0f,0f,-10f);
        var StartDirection=Vector3.Normalize(Vector3.Zero-Origin);
        Vector3 Target=new(2f,-3f,0f);

        Assert.True(MoveGizmoDrag.TryBegin(Frame,GizmoHandleId.PlaneXY,Origin,StartDirection,Vector3.UnitZ,out MoveGizmoDrag?Drag));
        Assert.True(Drag!.TryUpdate(Origin,Vector3.Normalize(Target-Origin),out Vector3 Delta));

        Assert.Equal(2f,Delta.X,4);
        Assert.Equal(-3f,Delta.Y,4);
        Assert.Equal(0f,Delta.Z,4);
    }

    [Fact]
    public void LocalFrame_RotatesTheHandleAxesWithThePrimaryRotation(){
        var Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Math.PI/2f);

        var Frame=MoveGizmoFrame.Create(Vector3.Zero,Rotation,SceneTransformSpace.Local,WorldScale:1f);

        Assert.Equal(0f,Frame.AxisX.X,4);
        Assert.Equal(0f,Frame.AxisX.Y,4);
        Assert.Equal(-1f,Frame.AxisX.Z,4);
    }

    [Fact]
    public void TryHitMove_DetectsAxisAndPlaneAnalyticShapes(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Axis=TransformGizmo.TryHitMove(
            Frame,new Vector3(0.7f,0f,-10f),Vector3.UnitZ,out _);
        GizmoHandleId?Plane=TransformGizmo.TryHitMove(
            Frame,new Vector3(0.3f,0.3f,-10f),Vector3.UnitZ,out _);

        Assert.Equal(GizmoHandleId.AxisX,Axis);
        Assert.Equal(GizmoHandleId.PlaneXY,Plane);
    }

    [Fact]
    public void WorldPositiveXHandle_ProjectsToScreenLeftUsingTheRuntimeCameraConvention(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        var ViewProjection=
            SceneViewCamera.GetView(Vector3.Zero,Yaw:0f,Pitch:0f,FocusDistance:10f)
            *SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        var Clip=Vector4.Transform(new Vector4(Frame.Pivot+Frame.AxisX,1f),ViewProjection);
        var NdcX=Clip.X/Clip.W;

        Assert.True(NdcX<0f,$"+Xギズモは既存のゲーム行列規約では画面左へ写る。NDC X={NdcX}");
    }

    [Fact]
    public void BuildMoveOverlay_GeneratesSolidAxisShaftsAndArrowheadsInAdditionToPlanes(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=TransformGizmo.BuildMoveOverlay(Frame,null,null);
        var TriangleVertices=Overlay
                            .Where(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle)
                            .Sum(static Primitive=>Primitive.Vertices.Count);

        Assert.True(TriangleVertices>18,"平面ハンドル3枚だけでなく、軸の円柱と円錐もsolid描画する");
    }

    [Fact]
    public void DisabledMoveHandle_IsFadedAndCannotBeHit(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        HashSet<GizmoHandleId>Disabled=[GizmoHandleId.AxisX];

        GizmoHandleId?Hit=TransformGizmo.TryHitMove(
            Frame,new Vector3(0.7f,0f,-10f),Vector3.UnitZ,out _,Disabled);
        IReadOnlyList<GizmoOverlayPrimitive>Overlay=TransformGizmo.BuildMoveOverlay(
            Frame,Hovered:null,Active:null,Disabled);

        Assert.Null(Hit);
        Assert.True(Overlay[0].Vertices[0].Color.W<1f);
    }

    [Fact]
    public void BuildMarqueeOverlay_ReusesTriangleAndLinePrimitivesAtScreenRectangleCorners(){
        var Overlay=MarqueeOverlay.Build(
                                        ScreenRectangle.FromPoints(20,20,80,80),
                                        Matrix4x4.Identity,Matrix4x4.Identity,ViewportWidth:100,ViewportHeight:100);

        var Fill=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle);
        var Outline=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);
        Assert.Equal(6,Fill.Vertices.Count);
        Assert.Equal(8,Outline.Vertices.Count);
        Assert.Equal(-0.6f,Outline.Vertices[0].Position.X,4);
        Assert.Equal(0.6f,Outline.Vertices[0].Position.Y,4);
        Assert.Equal(0.6f,Outline.Vertices[1].Position.X,4);
        Assert.Equal(0.6f,Outline.Vertices[1].Position.Y,4);
    }

    [Fact]
    public void TryHitRotate_RayTouchesZRing_ReturnsRotateZ(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        // M0033: RotateRingRadiusは0.9→0.78(Move/Scaleと重ならないよう再配置)。
        var Hit=TransformGizmo.TryHitRotate(
                                           Frame,new Vector3(0.78f,0f,-10f),Vector3.UnitZ,CameraForward:Vector3.UnitZ,out _);

        Assert.Equal(GizmoHandleId.RotateZ,Hit);
    }

    [Fact]
    public void RotateDrag_QuarterTurnAroundZ_ReturnsPositiveNinetyDegrees(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Assert.True(RotateGizmoDrag.TryBegin(
            Frame,GizmoHandleId.RotateZ,
            new Vector3(1f,0f,-10f),Vector3.UnitZ,Vector3.UnitZ,out RotateGizmoDrag?Drag));

        Assert.True(Drag!.TryUpdate(
            new Vector3(0f,1f,-10f),Vector3.UnitZ,out float Radians,out Quaternion Delta));

        Assert.Equal((float)(Math.PI/2d),Radians,4);
        var Rotated=Vector3.Transform(Vector3.UnitX,Delta);
        Assert.Equal(0f,Rotated.X,4);
        Assert.Equal(1f,Rotated.Y,4);
    }

    [Fact]
    public void BuildRotateOverlay_GeneratesThreeAxisRingsAndViewRing(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Overlay=TransformGizmo.BuildRotateOverlay(
                                                     Frame,CameraForward:Vector3.UnitZ,Hovered:null,Active:null);
        IReadOnlyList<GizmoOverlayPrimitive>Outlines=Overlay
            .Where(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line).ToArray();

        // M0035: 線の輪郭(4本、既存の検証)に加えて、面積を持つ帯(Triangle、4本)も生成する。
        Assert.Equal(4,Outlines.Count);
        Assert.All(Outlines,Primitive=>Assert.True(Primitive.Vertices.Count>=48));
        Assert.Equal(4,Overlay.Count(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle));
    }

    [Fact]
    public void TryHitScale_RayAtCenterAndXAxis_ReturnsExpectedHandles(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Center=TransformGizmo.TryHitScale(
                                             Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,out _);
        var Axis=TransformGizmo.TryHitScale(
                                           Frame,new Vector3(0.7f,0f,-10f),Vector3.UnitZ,out _);

        Assert.Equal(GizmoHandleId.ScaleUniform,Center);
        Assert.Equal(GizmoHandleId.ScaleX,Axis);
    }

    [Fact]
    public void ScaleDrag_XAxisDoublesOnlyXFactor(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Assert.True(ScaleGizmoDrag.TryBegin(
            Frame,GizmoHandleId.ScaleX,
            new Vector3(0.7f,0f,-10f),Vector3.UnitZ,Vector3.UnitZ,out ScaleGizmoDrag?Drag));

        Assert.True(Drag!.TryUpdate(
            new Vector3(1.4f,0f,-10f),Vector3.UnitZ,out Vector3 Factors));

        Assert.Equal(2f,Factors.X,4);
        Assert.Equal(1f,Factors.Y,4);
        Assert.Equal(1f,Factors.Z,4);
    }

    [Fact]
    public void BuildScaleOverlay_GeneratesSolidAxisShaftsAndCubes(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Overlay=TransformGizmo.BuildScaleOverlay(
                                                    Frame,Hovered:null,Active:null);

        Assert.True(Overlay.Count(Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line)>=3);
        Assert.True(Overlay.Count(Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle)>=7);
    }

    [Fact]
    public void TransformSnap_UsesMoveAngleAndScaleIncrements(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        Vector3 Move=TransformSnap.SnapTranslation(
            new Vector3(1.4f,2.6f,0f),Frame,GizmoHandleId.PlaneXY);
        float Rotation=TransformSnap.SnapRotationRadians((float)(20d*Math.PI/180d));
        Vector3 Scale=TransformSnap.SnapScaleFactors(
            new Vector3(1.16f,1f,1f),GizmoHandleId.ScaleX);

        Assert.Equal(new Vector3(1f,3f,0f),Move);
        Assert.Equal((float)(15d*Math.PI/180d),Rotation,5);
        Assert.Equal(new Vector3(1.2f,1f,1f),Scale);
    }

    // ----- M0033: Move/Rotate/Scale常時同時表示 -----

    [Fact]
    public void TryHitTransform_RayOffAxisAtRotateRingRadius_ReturnsRotateZNotAxisHandles(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        // 軸線から外れた45°の点はRotateZリング(半径0.78)の円周上だがMoveのAxisX/AxisY
        // セグメント(Y=0またはX=0)にもMoveのPlaneXY当たり判定(u,v<=0.48)にも触れない。
        var Offset=0.78f/(float)Math.Sqrt(2d);

        GizmoHandleId?Hit=TransformGizmo.TryHitTransform(
            Frame,new Vector3(Offset,Offset,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY);

        Assert.Equal(GizmoHandleId.RotateZ,Hit);
    }

    [Fact]
    public void TryHitTransform_RayAtPivot_ReturnsScaleUniform(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Hit=TransformGizmo.TryHitTransform(
            Frame,new Vector3(0f,0f,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY);

        Assert.Equal(GizmoHandleId.ScaleUniform,Hit);
    }

    [Fact]
    public void TryHitTransform_MovePlaneHandleStillReachableAfterRadiusRedesign(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Hit=TransformGizmo.TryHitTransform(
            Frame,new Vector3(0.3f,0.3f,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY);

        Assert.Equal(GizmoHandleId.PlaneXY,Hit);
    }

    [Fact]
    public void TryHitTransform_RayNearMoveArrowTip_ReturnsAxisX(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        // M0033のMove矢印先端(AxisLength=1.35)付近。ScaleAxisLength=1.0/RotateRingRadius=0.78
        // の帯より外側にあるため、Moveだけが応答するはずの領域。
        GizmoHandleId?Hit=TransformGizmo.TryHitTransform(
            Frame,new Vector3(1.3f,0f,-10f),Vector3.UnitZ,
            CameraForward:Vector3.UnitZ,CameraRight:Vector3.UnitX,CameraUp:Vector3.UnitY);

        Assert.Equal(GizmoHandleId.AxisX,Hit);
    }

    [Fact]
    public void TransformSpaceToggleGizmo_TryHit_DetectsRayAtBadgePosition(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Vector3 Position=TransformSpaceToggleGizmo.ComputePosition(Frame,Vector3.UnitX,Vector3.UnitY);

        GizmoHandleId?Hit=TransformSpaceToggleGizmo.TryHit(
            Frame,Vector3.UnitX,Vector3.UnitY,Position+new Vector3(0f,0f,-10f),Vector3.UnitZ,out float RayDistance);

        Assert.Equal(GizmoHandleId.TransformSpaceToggle,Hit);
        Assert.Equal(10f,RayDistance,4);
    }

    [Fact]
    public void TransformSpaceToggleGizmo_TryHit_MissesFarAwayRay(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Hit=TransformSpaceToggleGizmo.TryHit(
            Frame,Vector3.UnitX,Vector3.UnitY,new Vector3(0f,0f,-10f),Vector3.UnitZ,out _);

        Assert.Null(Hit);
    }

    [Fact]
    public void TransformSpaceToggleGizmo_BuildOverlay_LocalAndWorldProduceDifferentVertexCounts(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        IReadOnlyList<GizmoOverlayPrimitive>LocalOverlay=TransformSpaceToggleGizmo.BuildOverlay(
            Frame,Vector3.UnitX,Vector3.UnitY,SceneTransformSpace.Local,Hovered:null);
        IReadOnlyList<GizmoOverlayPrimitive>WorldOverlay=TransformSpaceToggleGizmo.BuildOverlay(
            Frame,Vector3.UnitX,Vector3.UnitY,SceneTransformSpace.World,Hovered:null);

        Assert.NotEqual(
            LocalOverlay.Sum(static Primitive=>Primitive.Vertices.Count),
            WorldOverlay.Sum(static Primitive=>Primitive.Vertices.Count));
    }

    // ----- M0035: ギズモの視認性改善 -----

    [Fact]
    public void BuildScaleOverlay_ScaleUniformDefaultColor_IsDarkGrayNotWhite(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Overlay=TransformGizmo.BuildScaleOverlay(
                                                    Frame,Hovered:null,Active:null);
        var UniformCube=Overlay.Last(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle);

        Assert.True(UniformCube.Vertices[0].Color.X<0.9f,"既定色は白ではなく濃いグレーであるべき");
    }

    [Fact]
    public void BuildScaleOverlay_ScaleUniformHovered_IsBrighterThanDefault(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Default=TransformGizmo.BuildScaleOverlay(Frame,Hovered:null,Active:null);
        var Hovered=TransformGizmo.BuildScaleOverlay(
                                                    Frame,Hovered:GizmoHandleId.ScaleUniform,Active:null);
        var DefaultBrightness=Default.Last(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle).Vertices[0].Color.X;
        var HoveredBrightness=Hovered.Last(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle).Vertices[0].Color.X;

        Assert.True(HoveredBrightness>DefaultBrightness);
    }

    [Fact]
    public void BuildScaleOverlay_ScaleUniformHovered_AddsWhiteAndBlackOutlineLines(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Default=TransformGizmo.BuildScaleOverlay(Frame,Hovered:null,Active:null);
        var Hovered=TransformGizmo.BuildScaleOverlay(
                                                    Frame,Hovered:GizmoHandleId.ScaleUniform,Active:null);

        var DefaultHasOutline=Default.Any(static Primitive=>
                                              Primitive.Kind==GizmoPrimitiveKind.Line&&Primitive.Vertices.Count==24);
        var OutlineLines=Hovered
                       .Where(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line&&Primitive.Vertices.Count==24)
                       .ToArray();

        Assert.False(DefaultHasOutline,"hoverしていない既定状態には輪郭線を追加しない");
        Assert.Equal(2,OutlineLines.Length);
        Assert.Contains(OutlineLines,Primitive=>
            Primitive.Vertices[0].Color==new Vector4(1f,1f,1f,1f));
        Assert.Contains(OutlineLines,Primitive=>
            Primitive.Vertices[0].Color==new Vector4(0f,0f,0f,1f));
    }

    [Fact]
    public void TryHitMove_RayInsideOldAxisStart_NoLongerHitsAxisX(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        // M0035: AxisStartは0.12→0.26。旧値の内側(0.15)はScaleUniformキューブに近すぎるため、
        // もうAxisXへヒットしないはず。
        GizmoHandleId?Hit=TransformGizmo.TryHitMove(Frame,new Vector3(0.15f,0f,-10f),Vector3.UnitZ,out _);

        Assert.NotEqual(GizmoHandleId.AxisX,Hit);
    }

    [Fact]
    public void BuildRotateOverlay_GeneratesFilledBandInAdditionToOutlineRings(){
        var Frame=MoveGizmoFrame.Create(
                                        Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        var Overlay=TransformGizmo.BuildRotateOverlay(
                                                     Frame,CameraForward:Vector3.UnitZ,Hovered:null,Active:null);

        var LineCount=Overlay.Count(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);
        var TriangleVertices=Overlay
                            .Where(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle)
                            .Sum(static Primitive=>Primitive.Vertices.Count);

        Assert.Equal(4,LineCount);
        Assert.True(TriangleVertices>0,"リングは細い線だけでなく面積を持つ帯も持つべき");
    }
}
