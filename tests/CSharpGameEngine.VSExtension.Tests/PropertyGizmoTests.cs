using System;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PropertyGizmoTests {
    private const string Components="""
        namespace CSharpGameEngine.Runtime.Windows {
            public enum CameraProjection { Perspective, Orthographic }
            public struct Camera {
                public bool Active; public CameraProjection Projection;
                public float FovYRadians; public float NearZ; public float FarZ; public float OrthographicSize;
            }
        }
        namespace CSharpGameEngine.Runtime.Windows {
            public struct DirectionalLight {
                public float DirectionTowardLightX; public float DirectionTowardLightY; public float DirectionTowardLightZ;
            }
            public struct PointLight { public float PositionX; public float PositionY; public float PositionZ; public float Range; }
            public struct SpotLight {
                public float PositionX; public float PositionY; public float PositionZ;
                public float DirectionX; public float DirectionY; public float DirectionZ;
                public float Range; public float InnerConeAngleRadians; public float OuterConeAngleRadians;
            }
        }
        """;

    [Fact]
    public void CameraPerspective_BuildsFovNearAndFarFrustumHandles(){
        var Node=AnalyzeNode("new Camera { FovYRadians=1f,NearZ=0.2f,FarZ=20f,OrthographicSize=5f }");

        Assert.True(PropertyGizmoBinding.TryCreate(Node,Matrix4x4.Identity,WorldScale:2f,out PropertyGizmoFrame Frame));

        Assert.Equal(PropertyGizmoKind.CameraPerspective,Frame.Kind);
        Assert.Contains(GizmoHandleId.CameraFov,Frame.Handles);
        Assert.Contains(GizmoHandleId.CameraNear,Frame.Handles);
        Assert.Contains(GizmoHandleId.CameraFar,Frame.Handles);
        Assert.NotEmpty(FrustumGizmo.BuildOverlay(Frame,Hovered:null,Active:null));
    }

    [Fact]
    public void CameraOrthographic_UsesOrthographicSizeInsteadOfFovHandle(){
        var Node=AnalyzeNode(
                             "new Camera { Projection=CameraProjection.Orthographic,NearZ=0.1f,FarZ=50f,OrthographicSize=8f }");

        Assert.True(PropertyGizmoBinding.TryCreate(Node,Matrix4x4.Identity,WorldScale:1f,out PropertyGizmoFrame Frame));

        Assert.Equal(PropertyGizmoKind.CameraOrthographic,Frame.Kind);
        Assert.Contains(GizmoHandleId.CameraOrthographicSize,Frame.Handles);
        Assert.DoesNotContain(GizmoHandleId.CameraFov,Frame.Handles);
    }

    [Fact]
    public void CompoundGizmo_ComposesActiveTransformAndPropertyPrimitives(){
        var TransformFrame=MoveGizmoFrame.Create(
                                                 Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,WorldScale:1f);
        var Node=AnalyzeNode("new PointLight { PositionX=2f,Range=4f }");
        Assert.True(PropertyGizmoBinding.TryCreate(Node,Matrix4x4.Identity,WorldScale:1f,out PropertyGizmoFrame PropertyFrame));

        IReadOnlyList<GizmoOverlayPrimitive> Combined=CompoundGizmo.Compose(
            TransformGizmo.BuildMoveOverlay(TransformFrame,Hovered:null,Active:null),
            PropertyGizmo.BuildOverlay(PropertyFrame,Hovered:null,Active:null));

        Assert.True(Combined.Count>TransformGizmo.BuildMoveOverlay(TransformFrame,null,null).Count);
        Assert.Contains(GizmoHandleId.PointRange,PropertyFrame.Handles);
    }

    [Theory]
    [InlineData((int)GizmoHandleId.CameraFov)]
    [InlineData((int)GizmoHandleId.CameraNear)]
    [InlineData((int)GizmoHandleId.PointRange)]
    [InlineData((int)GizmoHandleId.SpotOuterCone)]
    public void PropertyModifierTable_AllowsCtrlButNotShiftOrAlt(int HandleValue){
        var Handle=(GizmoHandleId)HandleValue;
        var Applicability=PropertyGizmoModifierPolicy.Get(Handle);

        Assert.True(Applicability.Ctrl);
        Assert.False(Applicability.Shift);
        Assert.False(Applicability.Alt);
    }

    [Fact]
    public void FovDrag_WithCtrl_SnapsToFiveDegreeStepsAndClamps(){
        var Node=AnalyzeNode("new Camera { FovYRadians=1f,NearZ=0.1f,FarZ=100f }");
        Assert.True(PropertyGizmoBinding.TryCreate(Node,Matrix4x4.Identity,WorldScale:1f,out PropertyGizmoFrame Frame));
        PropertyGizmoDrag Drag=new(Frame,GizmoHandleId.CameraFov,StartX:100,StartY:100);

        PropertyGizmoFrame Updated=Drag.Update(X:100,Y:70,Snap:true);
        var Degrees=Updated.FovYRadians*180f/(float)Math.PI;

        Assert.Equal(0f,Degrees%5f,3);
        Assert.InRange(Degrees,1f,179f);
    }

    [Theory]
    [InlineData("new DirectionalLight { DirectionTowardLightZ=1f }",(int)PropertyGizmoKind.DirectionalLight,(int)GizmoHandleId.DirectionalDirection)]
    [InlineData("new PointLight { Range=3f }",(int)PropertyGizmoKind.PointLight,(int)GizmoHandleId.PointRange)]
    [InlineData("new SpotLight { DirectionZ=1f,Range=5f,InnerConeAngleRadians=0.2f,OuterConeAngleRadians=0.4f }",(int)PropertyGizmoKind.SpotLight,(int)GizmoHandleId.SpotOuterCone)]
    public void CurrentLightVocabulary_BuildsItsPropertyHandles(
        string ComponentExpression,int ExpectedKindValue,int ExpectedHandleValue){
        var Node=AnalyzeNode(ComponentExpression);

        Assert.True(PropertyGizmoBinding.TryCreate(Node,Matrix4x4.Identity,WorldScale:1f,out PropertyGizmoFrame Frame));
        Assert.Equal((PropertyGizmoKind)ExpectedKindValue,Frame.Kind);
        Assert.Contains((GizmoHandleId)ExpectedHandleValue,Frame.Handles);
    }
    [Fact]
    public void PhysicalSpotLight_UsesTransformPoseAndFullDegreeMembers(){
        var Node=AnalyzePhysicalNode(
            "new SpotLight { RangeMeters=12f,InnerConeFullAngleDegrees=30f,OuterConeFullAngleDegrees=50f }");
        var Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Math.PI/2f);
        var NodeWorld=Matrix4x4.CreateFromQuaternion(Rotation)*Matrix4x4.CreateTranslation(2f,3f,4f);

        Assert.True(PropertyGizmoBinding.TryCreate(Node,NodeWorld,WorldScale:1f,out PropertyGizmoFrame Frame));

        Assert.True(Frame.UsesPhysicalLightFields);
        Assert.Equal(new Vector3(2f,3f,4f),Frame.Pivot);
        Assert.InRange(Vector3.Distance(Frame.Forward,Vector3.UnitX),0f,0.0001f);
        Assert.Equal(12f,Frame.Range);
        Assert.Equal(30f*(float)Math.PI/360f,Frame.InnerConeAngleRadians,4);
        Assert.Equal(50f*(float)Math.PI/360f,Frame.OuterConeAngleRadians,4);
        Assert.DoesNotContain(GizmoHandleId.SpotDirection,Frame.Handles);
        Assert.Contains(GizmoHandleId.SpotInnerCone,Frame.Handles);

        var Operations=PropertyGizmo.CreateOperations(
            Frame,GizmoHandleId.SpotOuterCone,default,default,DocumentVersion:1);
        var Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(Operations));
        Assert.Equal("OuterConeFullAngleDegrees",Operation.MemberName);
        Assert.Equal("50f",Operation.ValueExpression);
    }

    [Fact]
    public void PhysicalDirectionalLight_UsesTransformDirectionWithoutLegacyDirectionHandle(){
        var Node=AnalyzePhysicalNode("new DirectionalLight { IlluminanceLux=1000f }");
        var Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitX,-(float)Math.PI/2f);
        var NodeWorld=Matrix4x4.CreateFromQuaternion(Rotation)*Matrix4x4.CreateTranslation(1f,2f,3f);

        Assert.True(PropertyGizmoBinding.TryCreate(Node,NodeWorld,WorldScale:1f,out PropertyGizmoFrame Frame));

        Assert.True(Frame.UsesPhysicalLightFields);
        Assert.Equal(new Vector3(1f,2f,3f),Frame.Pivot);
        Assert.InRange(Vector3.Distance(Frame.Forward,Vector3.UnitY),0f,0.0001f);
        Assert.DoesNotContain(GizmoHandleId.DirectionalDirection,Frame.Handles);
    }

    private static SceneNode AnalyzePhysicalNode(string ComponentExpression){
        var SceneSource=$$"""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() { Components=[{{ComponentExpression}}] };
                          }
                          """;
        var Model=SceneAnalysisTestHelper.Analyze([PhysicalComponents,SceneSource]);
        Assert.NotNull(Model.Root);
        return Model.Root!;
    }

    private const string PhysicalComponents="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct DirectionalLight { public float IlluminanceLux; }
            public struct SpotLight {
                public float RangeMeters;
                public float InnerConeFullAngleDegrees;
                public float OuterConeFullAngleDegrees;
            }
        }
        """;



    private static SceneNode AnalyzeNode(string ComponentExpression){
        var SceneSource=$$"""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() { Components=[{{ComponentExpression}}] };
                          }
                          """;
        var Model=SceneAnalysisTestHelper.Analyze([Components,SceneSource]);
        Assert.NotNull(Model.Root);
        return Model.Root!;
    }
}
