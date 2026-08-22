using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneCameraPreviewPolicyTests {
    private const string Components = """
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
            public enum CameraProjection { Perspective, Orthographic }
            public struct Camera {
                public CameraProjection Projection; public float FovYRadians;
                public float NearZ; public float FarZ; public float OrthographicSize;
            }
        }
        """;

    [Fact]
    public void Build_UsesSelectedCameraAndComposesParentTransform() {
        SceneModel Model = SceneAnalysisTestHelper.Analyze([Components, """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root", Components=[
                    new Transform { PositionX=10f, RotationW=1f, ScaleX=1f, ScaleY=1f, ScaleZ=1f }
                ], Children=[new Node { Name="CameraActor", Components=[
                    new Transform { PositionZ=2f, RotationW=1f, ScaleX=1f, ScaleY=1f, ScaleZ=1f },
                    new Camera { FovYRadians=1f, NearZ=0.2f, FarZ=50f }
                ]}] };
            }
            """]);

        NodePath Path = NodePath.Root.Append(0);
        IReadOnlyList<SceneCameraPreview> Cameras = SceneCameraPreviewBinding.Build(Model, [Path], 16f / 9f);

        SceneCameraPreview Camera = Assert.Single(Cameras);
        Assert.Equal(Path, Camera.NodePath);
        Assert.Equal("CameraActor", Camera.DisplayName);
        Assert.True(Camera.IsAvailable, Camera.ErrorMessage);
        Assert.Equal(10f, Camera.World.M41, 4);
        Assert.Equal(2f, Camera.World.M43, 4);
        Assert.Equal(0.2f, Camera.NearZ, 4);
        Assert.Equal(50f, Camera.FarZ, 4);
        Assert.True(Camera.Projection.M11 > 0f);
        Vector3 ViewPoint=Vector3.Transform(new Vector3(10f, 0f, 3f), Camera.View);
        Assert.Equal(0f, ViewPoint.X, 3);
        Assert.Equal(0f, ViewPoint.Y, 3);
        Assert.Equal(-1f, ViewPoint.Z, 3);

        SceneCameraPreview Card=Camera.WithAspectRatio(1f);
        Matrix4x4 Expected=SceneViewCamera.GetProjection(1f,1f,0.2f,50f);
        Assert.Equal(1f,Card.AspectRatio,4);
        Assert.Equal(Expected.M11,Card.Projection.M11,4);
        Assert.NotEqual(Camera.Projection.M11,Card.Projection.M11);
    }

    [Fact]
    public void Build_ReportsInvalidCameraValuesWithoutThrowing() {
        SceneModel Model = SceneAnalysisTestHelper.Analyze([Components, """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[
                    new Transform { RotationW=1f, ScaleX=1f, ScaleY=1f, ScaleZ=1f },
                    new Camera { NearZ=-1f, FarZ=2f }
                ] };
            }
            """]);

        IReadOnlyList<SceneCameraPreview> Cameras = SceneCameraPreviewBinding.Build(
            Model, [NodePath.Root], 1f);

        SceneCameraPreview Camera = Assert.Single(Cameras);
        Assert.False(Camera.IsAvailable);
        Assert.Contains("NearZ", Camera.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_IgnoresUnselectedCamerasAndCapsTheResult() {
        SceneModel Model = SceneAnalysisTestHelper.Analyze([Components, """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    new Node { Name="A", Components=[new Camera()] },
                    new Node { Name="B", Components=[new Camera()] },
                    new Node { Name="C", Components=[new Camera()] },
                    new Node { Name="D", Components=[new Camera()] }
                ] };
            }
            """]);

        IReadOnlyList<SceneCameraPreview> Cameras = SceneCameraPreviewBinding.Build(
            Model,
            [NodePath.Root.Append(3), NodePath.Root.Append(1), NodePath.Root.Append(2), NodePath.Root.Append(0)],
            1f,
            MaximumPreviewCount: 3);

        Assert.Equal(new[] { "D", "B", "C" }, Cameras.Select(static Camera => Camera.DisplayName));
    }

    [Fact]
    public void Layout_AlignsCardsToBottomAndKeepsSixteenByNine() {
        SceneCameraPreview[] Cameras = [
            CreatePreview(NodePath.Root.Append(0), "A"),
            CreatePreview(NodePath.Root.Append(1), "B"),
            CreatePreview(NodePath.Root.Append(2), "C")
        ];

        IReadOnlyList<SceneCameraPreviewLayoutItem> Layout = CameraPreviewLayoutPolicy.Build(1200f, 800f, Cameras);

        Assert.Equal(3, Layout.Count);
        Assert.All(Layout, Item => {
            Assert.Equal(16f / 9f, Item.Rect.Width / Item.Rect.Height, 3);
            Assert.Equal(784f, Item.Rect.Y + Item.Rect.Height, 3);
        });
        Assert.True(Layout[0].IsPrimary);
        Assert.False(Layout[1].IsPrimary);
        Assert.True(Layout[0].Rect.X < Layout[1].Rect.X);
        Assert.True(Layout[1].Rect.X < Layout[2].Rect.X);
    }

    [Fact]
    public void HitTest_ReturnsTopmostCardAndDoesNotConsumeOutsideClicks() {
        SceneCameraPreview[] Cameras = [
            CreatePreview(NodePath.Root.Append(0), "A"),
            CreatePreview(NodePath.Root.Append(1), "B")
        ];
        IReadOnlyList<SceneCameraPreviewLayoutItem> Layout = CameraPreviewLayoutPolicy.Build(800f, 600f, Cameras);

        SceneCameraPreviewRect Rect = Layout[1].Rect;
        Assert.True(CameraPreviewHitTestPolicy.TryHit(Layout, Rect.X + 1f, Rect.Y + 1f, out NodePath Hit));
        Assert.Equal(Cameras[1].NodePath, Hit);
        Assert.False(CameraPreviewHitTestPolicy.TryHit(Layout, 0f, 0f, out NodePath Outside));
    }

    [Fact]
    public void Build_ReportsMissingTransformWithNonEmptyMessage() {
        SceneModel Model = SceneAnalysisTestHelper.Analyze([Components, """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="CameraOnly", Components=[new Camera()] };
            }
            """]);

        IReadOnlyList<SceneCameraPreview> Cameras = SceneCameraPreviewBinding.Build(
            Model, [NodePath.Root], 16f / 9f);

        SceneCameraPreview Camera = Assert.Single(Cameras);
        Assert.False(Camera.IsAvailable);
        Assert.False(string.IsNullOrEmpty(Camera.ErrorMessage));
    }

    private static SceneCameraPreview CreatePreview(NodePath Path, string Name) =>
        new(Path, Name, SceneProjectionMode.Perspective, 16f / 9f, 1f, 0.1f, 100f, 10f,
            Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity);
}

public sealed class CameraPreviewDrawPolicyTests {
    [Fact]
    public void ShouldDraw_AlwaysDrawsOnTheMainViewport() {
        NodePath CameraNode = NodePath.Root.Append(0);
        NodePath OtherNode = NodePath.Root.Append(1);

        Assert.True(CameraPreviewDrawPolicy.ShouldDraw(0, CameraNode, CameraNode));
        Assert.True(CameraPreviewDrawPolicy.ShouldDraw(0, OtherNode, CameraNode));
    }

    [Fact]
    public void ShouldDraw_ExcludesOnlyTheCameraNodeItselfOnAPreviewPass() {
        NodePath CameraNode = NodePath.Root.Append(0);
        NodePath OtherNode = NodePath.Root.Append(1);

        Assert.False(CameraPreviewDrawPolicy.ShouldDraw(1, CameraNode, CameraNode));
        Assert.True(CameraPreviewDrawPolicy.ShouldDraw(1, OtherNode, CameraNode));
        Assert.False(CameraPreviewDrawPolicy.ShouldDraw(3, CameraNode, CameraNode));
        Assert.True(CameraPreviewDrawPolicy.ShouldDraw(3, OtherNode, CameraNode));
    }
}

public sealed class SceneConstantBufferLayoutTests {
    private const int AlignedSlotSize = 256;
    private const int CameraPassCount = 4; // 1 main viewport + SceneRenderer.MaxCameraPreviews(3)
    private const int FrameCount = 2;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(37)]
    public void WriteOffsets_StayWithinTheMappedRegion_ForEveryFrameAndPass(int ObjectCount) {
        int ObjectCapacity = Math.Max(1, ObjectCount * 2);
        int AllocatedSize = SceneConstantBufferLayout.ComputeAllocatedSize(
            AlignedSlotSize, ObjectCapacity, CameraPassCount, FrameCount);

        for (int FrameIndex = 0; FrameIndex < FrameCount; FrameIndex++) {
            int FrameOffset = SceneConstantBufferLayout.ComputeFrameOffset(
                FrameIndex, AlignedSlotSize, ObjectCapacity, CameraPassCount);
            int MapLength = SceneConstantBufferLayout.ComputeMapLength(
                AlignedSlotSize, ObjectCapacity, CameraPassCount);

            // Map()に渡す長さそのものが確保済みバッファ内に収まっていること。
            Assert.True(FrameOffset + MapLength <= AllocatedSize,
                $"frame={FrameIndex} objects={ObjectCount}: FrameOffset+MapLength={FrameOffset + MapLength} exceeds AllocatedSize={AllocatedSize}");

            for (int Pass = 0; Pass < CameraPassCount; Pass++) {
                for (int ObjectIndex = 0; ObjectIndex < ObjectCount; ObjectIndex++) {
                    int WriteOffset = SceneConstantBufferLayout.ComputeWriteOffset(
                        FrameOffset, Pass, ObjectIndex, AlignedSlotSize, ObjectCapacity);
                    int WriteEnd = WriteOffset + AlignedSlotSize;

                    Assert.True(WriteOffset >= FrameOffset,
                        $"frame={FrameIndex} pass={Pass} object={ObjectIndex}: WriteOffset={WriteOffset} precedes FrameOffset={FrameOffset}");
                    Assert.True(WriteEnd <= FrameOffset + MapLength,
                        $"frame={FrameIndex} pass={Pass} object={ObjectIndex}: WriteEnd={WriteEnd} exceeds mapped region {FrameOffset + MapLength}");
                }
            }
        }
    }
}
