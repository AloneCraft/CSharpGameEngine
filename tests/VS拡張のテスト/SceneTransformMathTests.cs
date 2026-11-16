using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneTransformMathTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;
    [Fact]
    public void ToWorld_ComposesLocalBeforeParentWorld(){
        SceneTransformValue Parent=new(new Vector3(10f,0f,0f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Math.PI/2f),Vector3.One);
        SceneTransformValue Child=new(new Vector3(1f,0f,0f),Quaternion.Identity,Vector3.One);

        var World=SceneTransformMath.ToWorld(Child,SceneTransformMath.ToMatrix(Parent));

        Assert.Equal(10f,World.Translation.X,3);
        Assert.Equal(0f,World.Translation.Y,3);
        Assert.Equal(-1f,World.Translation.Z,3);
    }

    [Fact]
    public void TryCalculateLocal_RoundTripsThroughParentWorld(){
        SceneTransformValue Expected=new(
            new Vector3(2f,3f,4f),
            Quaternion.CreateFromYawPitchRoll(0.3f,-0.2f,0.1f),
            new Vector3(2f,1.5f,0.75f));
        var Parent=Matrix4x4.CreateFromYawPitchRoll(-0.4f,0.15f,0.2f)*Matrix4x4.CreateTranslation(5f,-2f,8f);
        var World=SceneTransformMath.ToWorld(Expected,Parent);

        var Succeeded=SceneTransformMath.TryCalculateLocal(World,Parent,out var Actual);

        Assert.True(Succeeded);
        AssertVector(Expected.Position,Actual.Position);
        AssertVector(Expected.Scale,Actual.Scale);
        Assert.True(Math.Abs(Quaternion.Dot(Expected.Rotation,Actual.Rotation))>0.9999f);
    }

    [Fact]
    public void TryCalculateLocal_SingularParent_ReturnsFalse(){
        var Parent=Matrix4x4.CreateScale(0f,1f,1f);

        Assert.False(SceneTransformMath.TryCalculateLocal(Matrix4x4.Identity,Parent,out _));
    }

    [Fact]
    public void CanEditTransform_RotateRequiresEveryPositionAndRotationMember(){
        var Node=AnalyzeRoot("""
                             using CSharpGameEngine.Entities;
                             using CSharpGameEngine.Runtime.Windows;
                             public partial class MainScene:IScene {
                                 private static float Read()=>1f;
                                 public Node Root=>new() { Components=[new Transform {
                                     PositionX=Read(),RotationW=1f,
                                 }]};
                             }
                             """);

        Assert.False(SceneTransformBinding.CanEditTransform(
            Node,SceneActiveTool.Rotate,GizmoHandleId.RotateZ));
    }

    [Fact]
    public void CanEditTransform_ScaleRequiresEveryScaleMember(){
        var Node=AnalyzeRoot("""
                             using CSharpGameEngine.Entities;
                             using CSharpGameEngine.Runtime.Windows;
                             public partial class MainScene:IScene {
                                 private static float Read()=>1f;
                                 public Node Root=>new() { Components=[new Transform {
                                     RotationW=1f,ScaleX=1f,ScaleY=Read(),ScaleZ=1f,
                                 }]};
                             }
                             """);

        Assert.False(SceneTransformBinding.CanEditTransform(
            Node,SceneActiveTool.Scale,GizmoHandleId.ScaleX));
        Assert.False(SceneTransformBinding.CanEditTransform(
            Node,SceneActiveTool.Scale,GizmoHandleId.ScaleUniform));
    }

    [Fact]
    public void CanEditTransform_SurfaceSnapPlane_RequiresEveryPositionAndRotationMember(){
        // M0034: SurfaceSnapPlaneはPosition+Rotationの両方を書き換えるため、Toolに依らず
        // CanEditRotationと同じ要件(全Position+全Rotationが編集可能)をHandle優先で判定する。
        var Node=AnalyzeRoot("""
                             using CSharpGameEngine.Entities;
                             using CSharpGameEngine.Runtime.Windows;
                             public partial class MainScene:IScene {
                                 private static float Read()=>1f;
                                 public Node Root=>new() { Components=[new Transform {
                                     PositionX=Read(),RotationW=1f,
                                 }]};
                             }
                             """);

        Assert.False(SceneTransformBinding.CanEditTransform(
            Node,SceneActiveTool.Move,GizmoHandleId.SurfaceSnapPlane));
    }

    [Fact]
    public void CanEditTransform_SurfaceSnapPlane_TrueWhenPositionAndRotationAreEditable(){
        var Node=AnalyzeRoot("""
                             using CSharpGameEngine.Entities;
                             using CSharpGameEngine.Runtime.Windows;
                             public partial class MainScene:IScene {
                                 public Node Root=>new() { Components=[new Transform()]};
                             }
                             """);

        Assert.True(SceneTransformBinding.CanEditTransform(
            Node,SceneActiveTool.Move,GizmoHandleId.SurfaceSnapPlane));
    }

    private static SceneNode AnalyzeRoot(string SceneSource){
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,SceneSource]);
        return Assert.IsType<SceneNode>(Model.Root);
    }

    private static void AssertVector(Vector3 Expected,Vector3 Actual){
        Assert.Equal(Expected.X,Actual.X,4);
        Assert.Equal(Expected.Y,Actual.Y,4);
        Assert.Equal(Expected.Z,Actual.Z,4);
    }
}
