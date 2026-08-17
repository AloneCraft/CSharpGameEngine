using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class GizmoDragHudTests {
    [Fact]
    public void AccumulateAngle_SmoothIncrementsAcrossMultipleWraps_MatchesTotalRotation(){
        // 5°刻みで144ステップ=720°分、連続的にRawを進める(Atan2出力を模倣: (-π,π]で折り返る)。
        float Accumulated=0f;
        float PreviousRaw=0f;
        float TrueTotalDegrees=0f;
        for(var Step=1;Step<=144;Step++){
            TrueTotalDegrees=Step*5f;
            float RawRadians=WrapToPi(TrueTotalDegrees*(float)(Math.PI/180d));
            Accumulated=GizmoDragHud.AccumulateAngle(Accumulated,PreviousRaw,RawRadians);
            PreviousRaw=RawRadians;
        }

        Assert.Equal(TrueTotalDegrees,Accumulated*(180f/(float)Math.PI),1);
    }

    [Fact]
    public void AccumulateAngle_CrossingPositivePiWrap_ContinuesWithoutSignFlip(){
        float Raw170=170f*(float)(Math.PI/180d);
        float RawNeg170=-170f*(float)(Math.PI/180d); // Atan2が+190°をこう折り返す。
        float Accumulated=GizmoDragHud.AccumulateAngle(0f,0f,Raw170);
        Accumulated=GizmoDragHud.AccumulateAngle(Accumulated,Raw170,RawNeg170);

        Assert.Equal(190f,Accumulated*(180f/(float)Math.PI),1);
    }

    [Fact]
    public void AccumulateAngle_CrossingNegativePiWrap_ContinuesWithoutSignFlip(){
        float RawNeg170=-170f*(float)(Math.PI/180d);
        float Raw170=170f*(float)(Math.PI/180d); // Atan2が-190°をこう折り返す。
        float Accumulated=GizmoDragHud.AccumulateAngle(0f,0f,RawNeg170);
        Accumulated=GizmoDragHud.AccumulateAngle(Accumulated,RawNeg170,Raw170);

        Assert.Equal(-190f,Accumulated*(180f/(float)Math.PI),1);
    }

    private static float WrapToPi(float Radians){
        while(Radians>(float)Math.PI){ Radians-=(float)(Math.PI*2d); }
        while(Radians<=-(float)Math.PI){ Radians+=(float)(Math.PI*2d); }
        return Radians;
    }

    [Theory]
    [InlineData('0')] [InlineData('1')] [InlineData('2')] [InlineData('3')] [InlineData('4')]
    [InlineData('5')] [InlineData('6')] [InlineData('7')] [InlineData('8')] [InlineData('9')]
    [InlineData('.')] [InlineData('-')] [InlineData('+')] [InlineData('°')] [InlineData('x')]
    [InlineData('c')] [InlineData('m')]
    public void BuildScreenText_SupportedCharacter_ProducesAtLeastOneSegment(char Character){
        IReadOnlyList<GizmoOverlayVertex>Vertices=GizmoDragHud.BuildScreenText(
            Character.ToString(),0f,0f,14f,new Vector4(1f,1f,0f,1f),Matrix4x4.Identity,100f,100f);

        Assert.True(Vertices.Count>=2);
        Assert.Equal(0,Vertices.Count%2); // Lineプリミティブなので頂点数は偶数(2点で1本)。
    }

    [Fact]
    public void BuildScreenText_UnsupportedCharacter_ProducesNoVertices(){
        IReadOnlyList<GizmoOverlayVertex>Vertices=GizmoDragHud.BuildScreenText(
            "q",0f,0f,14f,new Vector4(1f,1f,0f,1f),Matrix4x4.Identity,100f,100f);

        Assert.Empty(Vertices);
    }

    [Fact]
    public void BuildScreenText_LongerString_ProducesMoreVerticesThanShorter(){
        IReadOnlyList<GizmoOverlayVertex>Short=GizmoDragHud.BuildScreenText(
            "1",0f,0f,14f,Vector4.One,Matrix4x4.Identity,100f,100f);
        IReadOnlyList<GizmoOverlayVertex>Long=GizmoDragHud.BuildScreenText(
            "12345",0f,0f,14f,Vector4.One,Matrix4x4.Identity,100f,100f);

        Assert.True(Long.Count>Short.Count);
    }

    [Fact]
    public void Build_RotateNinetyDegreeSweep_WedgeOutlineStartsAndEndsAtExpectedDirections(){
        Vector3 Pivot=new(1f,2f,3f);
        float Radius=TransformGizmo.RotateRingRadius; // WorldScale=1なのでそのまま半径。
        GizmoDragHudState State=new(
            SceneActiveTool.Rotate,GizmoHandleId.RotateZ,Pivot,Pivot,Pivot,
            Vector3.UnitZ,Vector3.UnitX,(float)(Math.PI/2d),TransformGizmo.RotateRingRadius,
            Vector3.One,WorldScale:1f,CursorX:0,CursorY:0);
        Matrix4x4 View=SceneViewCamera.GetView(Vector3.Zero,Yaw:0f,Pitch:0f,FocusDistance:10f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=GizmoDragHud.Build(State,View,Projection,200f,200f);
        GizmoOverlayPrimitive Outline=Overlay.First(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);

        // 90°回転(Z軸周り)なのでX→Yへ。輪郭はPivotで始まり[1]が開始方向、[^2]が終了方向、[^1]がPivot。
        AssertClose(Pivot,Outline.Vertices[0].Position);
        AssertClose(Pivot+(Vector3.UnitX*Radius),Outline.Vertices[1].Position);
        AssertClose(Pivot+(Vector3.UnitY*Radius),Outline.Vertices[^2].Position);
        AssertClose(Pivot,Outline.Vertices[^1].Position);
    }

    [Fact]
    public void Build_RotateHud_StaysWellUnderVertexBudget(){
        GizmoDragHudState State=new(
            SceneActiveTool.Rotate,GizmoHandleId.RotateZ,Vector3.Zero,Vector3.Zero,Vector3.Zero,
            Vector3.UnitZ,Vector3.UnitX,(float)(Math.PI*1.9d),TransformGizmo.RotateRingRadius,
            Vector3.One,WorldScale:1f,CursorX:100,CursorY:100);
        Matrix4x4 View=SceneViewCamera.GetView(new Vector3(0f,0f,-10f),Yaw:0f,Pitch:0f,FocusDistance:10f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=GizmoDragHud.Build(State,View,Projection,400f,400f);
        int TotalVertices=Overlay.Sum(static Primitive=>Primitive.Vertices.Count);

        Assert.True(TotalVertices<1000,$"Rotate HUD(最大級のスイープ)は1000頂点未満に収まるべき、実測{TotalVertices}");
    }

    [Fact]
    public void Build_Move_DashedLineSpansExactlyFromStartToCurrentPivot(){
        Vector3 Start=Vector3.Zero;
        Vector3 Current=new(5f,0f,0f);
        GizmoDragHudState State=new(
            SceneActiveTool.Move,GizmoHandleId.AxisX,Start,Start,Current,
            Vector3.UnitY,Vector3.UnitX,0f,1f,Vector3.One,WorldScale:1f,CursorX:0,CursorY:0);
        Matrix4x4 View=SceneViewCamera.GetView(new Vector3(0f,0f,-20f),Yaw:0f,Pitch:0f,FocusDistance:20f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=GizmoDragHud.Build(State,View,Projection,200f,200f);
        GizmoOverlayPrimitive DashedLine=Overlay.First(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line);

        AssertClose(Start,DashedLine.Vertices[0].Position);
        AssertClose(Current,DashedLine.Vertices[^1].Position);
    }

    [Fact]
    public void Build_Move_NoDisplacement_ProducesNoDashedLine(){
        GizmoDragHudState State=new(
            SceneActiveTool.Move,GizmoHandleId.AxisX,Vector3.Zero,Vector3.Zero,Vector3.Zero,
            Vector3.UnitY,Vector3.UnitX,0f,1f,Vector3.One,WorldScale:1f,CursorX:0,CursorY:0);
        Matrix4x4 View=SceneViewCamera.GetView(new Vector3(0f,0f,-20f),Yaw:0f,Pitch:0f,FocusDistance:20f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=GizmoDragHud.Build(State,View,Projection,200f,200f);

        // 移動量"0.0"のラベル自体はLineプリミティブとして残るため、Line種の数が
        // 1つ(ラベルのみ)であること=破線が追加されていないことを確認する。
        Assert.Equal(1,Overlay.Count(static Primitive=>Primitive.Kind==GizmoPrimitiveKind.Line));
    }

    [Fact]
    public void Build_ScaleSingleAxis_ProducesExactlyOneLabel(){
        GizmoDragHudState State=new(
            SceneActiveTool.Scale,GizmoHandleId.ScaleX,Vector3.Zero,Vector3.Zero,Vector3.Zero,
            Vector3.UnitY,Vector3.UnitX,0f,1f,new Vector3(1.25f,1f,1f),WorldScale:1f,CursorX:50,CursorY:50);
        Matrix4x4 View=SceneViewCamera.GetView(new Vector3(0f,0f,-10f),Yaw:0f,Pitch:0f,FocusDistance:10f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=GizmoDragHud.Build(State,View,Projection,200f,200f);

        Assert.Single(Overlay);
    }

    [Fact]
    public void Build_ScaleUniform_ProducesExactlyThreeLabels(){
        GizmoDragHudState State=new(
            SceneActiveTool.Scale,GizmoHandleId.ScaleUniform,Vector3.Zero,Vector3.Zero,Vector3.Zero,
            Vector3.UnitY,Vector3.UnitX,0f,1f,new Vector3(1.5f,1.5f,1.5f),WorldScale:1f,CursorX:50,CursorY:50);
        Matrix4x4 View=SceneViewCamera.GetView(new Vector3(0f,0f,-10f),Yaw:0f,Pitch:0f,FocusDistance:10f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1f,NearZ:0.1f,FarZ:100f);

        IReadOnlyList<GizmoOverlayPrimitive>Overlay=GizmoDragHud.Build(State,View,Projection,200f,200f);

        Assert.Equal(3,Overlay.Count);
    }

    private static void AssertClose(Vector3 Expected,Vector3 Actual,float Tolerance=1e-3f)=>
        Assert.True((Expected-Actual).Length()<Tolerance,$"Expected {Expected} but was {Actual}");
}
