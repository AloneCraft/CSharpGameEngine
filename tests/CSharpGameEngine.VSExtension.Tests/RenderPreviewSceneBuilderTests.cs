using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class RenderPreviewSceneBuilderTests {
    /// <summary>
    /// M0096：ランタイムビューは、コミット時に書き込まれる値として、各ルートの親相対変換をドラッグで取得する。ランタイムビューのノードパスをキーにする。ここでは親が R=1 にあり、U 周りに 4 分の 1 回転しているため、ワールド位置 (1, 0.5, -2) は親に対して (2, 0.5, 0) となる。
    /// </summary>
    [Fact]
    public void DragOverrides_AreParentRelativeAndKeyedByNodePath(){
        var Parent=Matrix4x4.CreateRotationY((float)(Math.PI/2))*Matrix4x4.CreateTranslation(1f,0f,0f);
        var Child=NodePath.Root.Append(0);
        var World=Matrix4x4.CreateTranslation(2f,0.5f,0f)*Parent;
        var Overrides=RenderPreviewSceneBuilder.DragOverrides(
            [new SceneTransformTarget(new EditorNodeId(7),Child,Parent,Parent)],
            [new SceneTransformPreview(new EditorNodeId(7),Child,World)]);
        var Override=Assert.Single(Overrides);
        Assert.Equal("0",Override.ObjectId);
        Assert.Equal(2f,Override.Position[0],4);
        Assert.Equal(0.5f,Override.Position[1],4);
        Assert.Equal(0f,Override.Position[2],4);
        Assert.Equal([0f,0f,0f,1f],Override.Rotation.Select(static Value=>(float)Math.Round(Value,4)).ToArray());
        Assert.Equal([1f,1f,1f],Override.Scale.Select(static Value=>(float)Math.Round(Value,4)).ToArray());
    }

    [Fact]
    public void FromScene_PreservesStableIdentityRosterAndDeduplicatesSharedMesh(){
        SimpleMesh Mesh=CreateMesh();
        SceneRenderObject Root=new(){
            Mesh=Mesh,
            NodeId=NodePath.Root,
            World=Matrix4x4.Identity,
        };
        SceneRenderObject Child=new(){
            Mesh=Mesh,
            NodeId=NodePath.Root.Append(0),
            World=Matrix4x4.CreateTranslation(3f,4f,5f),
        };
        Matrix4x4 View=Matrix4x4.CreateLookAt(
            new Vector3(0f,0f,5f),Vector3.Zero,Vector3.UnitY);
        Matrix4x4 Projection=Matrix4x4.CreatePerspectiveFieldOfView(
            FloatMath.Pi/4f,4f/3f,0.1f,100f);

        RenderPreviewScene Scene=RenderPreviewSceneBuilder.FromScene(
            [Root,Child],View,Projection,new Vector3(0f,0f,5f));

        Assert.Single(Scene.Meshes);
        Assert.Single(Scene.Materials);
        Assert.Equal(["(root)","0"],Scene.Objects.Select(static Object=>Object.ObjectId));
        Assert.Equal(0,Scene.Objects[0].MeshIndex);
        Assert.Equal(0,Scene.Objects[1].MeshIndex);
        Assert.Equal(3f,Scene.Objects[1].World.Values[12]);
        Assert.Equal(4f,Scene.Objects[1].World.Values[13]);
        Assert.Equal(5f,Scene.Objects[1].World.Values[14]);
        Assert.Equal([0f,0f,5f],Scene.Camera!.Position);
        Assert.Null(RenderPreviewHostProtocol.Validate(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=Guid.NewGuid(),
            Sequence=1,
            Command=RenderPreviewHostCommand.Open,
            SurfaceHandle=123,
            Width=800,
            Height=600,
            ShaderArtifactPath="Stage.cgeshaderartifact",
            Scene=Scene,
        }));
    }

    [Fact]
    public void CreateFrame_UsesCompleteRosterAndUpdatedTransforms(){
        SimpleMesh Mesh=CreateMesh();
        SceneRenderObject Object=new(){
            Mesh=Mesh,
            NodeId=NodePath.Root.Append(2),
            World=Matrix4x4.CreateScale(2f),
        };

        RenderPreviewCameraPass CameraPreview=new(){
            X=1,Y=2,Width=32,Height=18,
            Camera=new RenderPreviewCamera {
                ViewProjection=RenderPreviewMatrix4x4.Identity,
                Position=[7f,8f,9f],
            },
            ExcludedObjectId="2",
        };
        RenderPreviewFrame Frame=RenderPreviewSceneBuilder.CreateFrame(
            [Object],Matrix4x4.Identity,Matrix4x4.Identity,new Vector3(1f,2f,3f),
            SceneViewRenderMode.Wireframe,[Object.NodeId],
            [
                new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,[
                    new GizmoOverlayVertex(Vector3.Zero,Vector4.One),
                    new GizmoOverlayVertex(Vector3.UnitX,Vector4.One),
                    new GizmoOverlayVertex(Vector3.UnitY,Vector4.One),
                ]),
                new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                    new GizmoOverlayVertex(Vector3.Zero,Vector4.One),
                    new GizmoOverlayVertex(Vector3.UnitZ,Vector4.One),
                ]),
            ],
            [CameraPreview]);

        RenderPreviewObjectTransform Transform=Assert.Single(Frame.Objects);
        Assert.Equal("2",Transform.ObjectId);
        Assert.Equal(2f,Transform.World.Values[0]);
        Assert.Equal(2f,Transform.World.Values[5]);
        Assert.Equal(2f,Transform.World.Values[10]);
        Assert.Equal(1f,Transform.SelectionWeight);
        Assert.Equal(RenderPreviewPresentationMode.Wireframe,Frame.PresentationMode);
        Assert.Equal([1f,2f,3f],Frame.Camera!.Position);
        Assert.Equal(3,Frame.OverlayTriangles.Length);
        Assert.Equal(2,Frame.OverlayLines.Length);
        Assert.Same(CameraPreview,Assert.Single(Frame.CameraPreviews));
    }

    [Fact]
    public void FromScene_EmptyAuthoringSceneRemainsEmptyOnWire(){
        RenderPreviewScene Scene=RenderPreviewSceneBuilder.FromScene(
            [],Matrix4x4.Identity,Matrix4x4.Identity,Vector3.Zero);

        Assert.Empty(Scene.Meshes);
        Assert.Empty(Scene.Materials);
        Assert.Empty(Scene.Objects);
    }

    [Fact]
    public void ResolvePackagedArtifacts_PrefersInstalledPreviewHostClosure(){
        string Root=Path.Combine(Path.GetTempPath(),$"CgePreviewResolver-{Guid.NewGuid():N}");
        string ExtensionDirectory=Path.Combine(Root,"Extension");
        string PreviewDirectory=Path.Combine(ExtensionDirectory,"PreviewHost");
        string ShaderDirectory=Path.Combine(PreviewDirectory,"Shaders");
        string HostPath=Path.Combine(
            PreviewDirectory,"CSharpGameEngine.EditorPreview.Host.exe");
        string ShaderPath=Path.Combine(ShaderDirectory,"Stage.cgeshaderartifact");
        Directory.CreateDirectory(ShaderDirectory);
        File.WriteAllBytes(HostPath,[1]);
        File.WriteAllBytes(ShaderPath,[2]);
        try {
            Assert.Equal(
                Path.GetFullPath(HostPath),
                RenderPreviewSceneBuilder.ResolveHostPath(
                    Root,ExtensionDirectory,OverridePath:null));
            Assert.Equal(
                Path.GetFullPath(ShaderPath),
                RenderPreviewSceneBuilder.ResolveShaderArtifactPath(
                    Root,ExtensionDirectory,OverridePath:null));
        } finally {
            Directory.Delete(Root,recursive:true);
        }
    }

    private static SimpleMesh CreateMesh()=>new(){
        Positions=[
            new Vector3(-0.5f,-0.5f,0f),
            new Vector3(0.5f,-0.5f,0f),
            new Vector3(0f,0.5f,0f),
        ],
        Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ],
        Indices=[0,1,2],
        BaseColorFactor=new Vector4(0.25f,0.5f,0.75f,1f),
        BoundsCenter=Vector3.Zero,
        BoundsRadius=1f,
    };
}
