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
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Vector3 StartOrigin=new(0f,0f,-10f);
        Vector3 StartDirection=Vector3.Normalize(Vector3.Zero-StartOrigin);
        Vector3 Target=new(2f,0f,0f);
        Vector3 CurrentDirection=Vector3.Normalize(Target-StartOrigin);

        Assert.True(MoveGizmoDrag.TryBegin(Frame,GizmoHandleId.AxisX,StartOrigin,StartDirection,Vector3.UnitZ,out MoveGizmoDrag?Drag));
        Assert.True(Drag!.TryUpdate(StartOrigin,CurrentDirection,out Vector3 Delta));

        Assert.Equal(2f,Delta.X,4);
        Assert.Equal(0f,Delta.Y,4);
        Assert.Equal(0f,Delta.Z,4);
    }

    [Fact]
    public void PlaneDrag_XYConstrainsDeltaToThePlane(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Vector3 Origin=new(0f,0f,-10f);
        Vector3 StartDirection=Vector3.Normalize(Vector3.Zero-Origin);
        Vector3 Target=new(2f,-3f,0f);

        Assert.True(MoveGizmoDrag.TryBegin(Frame,GizmoHandleId.PlaneXY,Origin,StartDirection,Vector3.UnitZ,out MoveGizmoDrag?Drag));
        Assert.True(Drag!.TryUpdate(Origin,Vector3.Normalize(Target-Origin),out Vector3 Delta));

        Assert.Equal(2f,Delta.X,4);
        Assert.Equal(-3f,Delta.Y,4);
        Assert.Equal(0f,Delta.Z,4);
    }

    [Fact]
    public void LocalFrame_RotatesTheHandleAxesWithThePrimaryRotation(){
        Quaternion Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Math.PI/2f);

        MoveGizmoFrame Frame=MoveGizmoFrame.Create(Vector3.Zero,Rotation,SceneTransformSpace.Local,WorldScale:1f);

        Assert.Equal(0f,Frame.AxisX.X,4);
        Assert.Equal(0f,Frame.AxisX.Y,4);
        Assert.Equal(-1f,Frame.AxisX.Z,4);
    }

    [Fact]
    public void TryHitMove_DetectsAxisAndPlaneAnalyticShapes(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        GizmoHandleId?Axis=TransformGizmo.TryHitMove(
            Frame,new Vector3(0.7f,0f,-10f),Vector3.UnitZ);
        GizmoHandleId?Plane=TransformGizmo.TryHitMove(
            Frame,new Vector3(0.3f,0.3f,-10f),Vector3.UnitZ);

        Assert.Equal(GizmoHandleId.AxisX,Axis);
        Assert.Equal(GizmoHandleId.PlaneXY,Plane);
    }

    [Fact]
    public void WorldPositiveXHandle_ProjectsToScreenLeftUsingTheRuntimeCameraConvention(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        Matrix4x4 ViewProjection=
            SceneViewCamera.GetView(Vector3.Zero,Yaw:0f,Pitch:0f,FocusDistance:10f)
            *SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        Vector4 Clip=Vector4.Transform(new Vector4(Frame.Pivot+Frame.AxisX,1f),ViewProjection);
        float NdcX=Clip.X/Clip.W;

        Assert.True(NdcX<0f,$"+Xギズモは既存のゲーム行列規約では画面左へ写る。NDC X={NdcX}");
    }

    [Fact]
    public void BuildMoveOverlay_GeneratesSolidAxisShaftsAndArrowheadsInAdditionToPlanes(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=TransformGizmo.BuildMoveOverlay(Frame,null,null);
        int TriangleVertices=Overlay
            .Where(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle)
            .Sum(static Primitive=>Primitive.Vertices.Count);

        Assert.True(TriangleVertices>18,"平面ハンドル3枚だけでなく、軸の円柱と円錐もsolid描画する");
    }

    [Fact]
    public void DisabledMoveHandle_IsFadedAndCannotBeHit(){
        MoveGizmoFrame Frame=MoveGizmoFrame.Create(
            Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        HashSet<GizmoHandleId>Disabled=[GizmoHandleId.AxisX];

        GizmoHandleId?Hit=TransformGizmo.TryHitMove(
            Frame,new Vector3(0.7f,0f,-10f),Vector3.UnitZ,Disabled);
        IReadOnlyList<GizmoOverlayPrimitive>Overlay=TransformGizmo.BuildMoveOverlay(
            Frame,Hovered:null,Active:null,Disabled);

        Assert.Null(Hit);
        Assert.True(Overlay[0].Vertices[0].Color.W<1f);
    }

    [Fact]
    public void BuildMarqueeOverlay_ReusesTriangleAndLinePrimitivesAtScreenRectangleCorners(){
        IReadOnlyList<GizmoOverlayPrimitive>Overlay=MarqueeOverlay.Build(
            ScreenRectangle.FromPoints(20,20,80,80),
            Matrix4x4.Identity,Matrix4x4.Identity,ViewportWidth:100,ViewportHeight:100);

        GizmoOverlayPrimitive Fill=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Triangle);
        GizmoOverlayPrimitive Outline=Assert.Single(Overlay,Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);
        Assert.Equal(6,Fill.Vertices.Count);
        Assert.Equal(8,Outline.Vertices.Count);
        Assert.Equal(-0.6f,Outline.Vertices[0].Position.X,4);
        Assert.Equal(0.6f,Outline.Vertices[0].Position.Y,4);
        Assert.Equal(0.6f,Outline.Vertices[1].Position.X,4);
        Assert.Equal(0.6f,Outline.Vertices[1].Position.Y,4);
    }
}
