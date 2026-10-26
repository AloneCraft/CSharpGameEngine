using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Contracts.Tests;

public sealed class RenderPreviewHostProtocolTests {
    /// <summary>
    /// M0097: version 7 removed the runtime diagnostics snapshot. The command is gone, so a request that still
    /// carries its old value is an unknown command. M0119: version 9 derives depth from projection.
    /// </summary>
    [Fact]
    public void Protocol_HasNoDiagnosticsSnapshotCommand(){
        Assert.Equal(9,RenderPreviewHostProtocol.CurrentVersion);
        Assert.DoesNotContain("Snapshot",Enum.GetNames(typeof(RenderPreviewHostCommand)));
        Assert.Null(typeof(RenderPreviewHostResponse).GetProperty("DebugSnapshot"));
        RenderPreviewHostRequest Request=CreateCommand((RenderPreviewHostCommand)6);
        Assert.Contains("unknown",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtocolV9_ValidatesNativePerspectiveCameras(){
        // Perspective metadata is mandatory even when the caller supplies only the default curve.
        var Perspective=System.Numerics.Matrix4x4.CreateLookAt(new(0f,1f,-4f),System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitY)
            *System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(0.8f,1.5f,0.1f,300f);
        RenderPreviewHostRequest Request=CreateCommand(RenderPreviewHostCommand.UpdateFrame);
        RenderPreviewCamera Camera=new(){
            ViewProjection=new RenderPreviewMatrix4x4 { Values=[
                Perspective.M11,Perspective.M12,Perspective.M13,Perspective.M14,
                Perspective.M21,Perspective.M22,Perspective.M23,Perspective.M24,
                Perspective.M31,Perspective.M32,Perspective.M33,Perspective.M34,
                Perspective.M41,Perspective.M42,Perspective.M43,Perspective.M44] },
            FovYRadians=0.8f,NearZ=0.1f,FarZ=300f,
        };
        Request.Frame=new RenderPreviewFrame { Camera=Camera };
        Assert.Equal(1f,Camera.RadialTransitionMeters);
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
        void AssertRejected(Action<RenderPreviewCamera>Change){
            var Saved=(Camera.ViewProjection,Camera.FovYRadians,Camera.NearZ,Camera.RadialTransitionMeters);
            Change(Camera);
            Assert.Contains("depth",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);
            (Camera.ViewProjection,Camera.FovYRadians,Camera.NearZ,Camera.RadialTransitionMeters)=Saved;
        }
        AssertRejected(static Value=>Value.FovYRadians=0f);
        AssertRejected(static Value=>Value.NearZ=0f);
        AssertRejected(static Value=>Value.RadialTransitionMeters=0f);
        Camera.ViewProjection=RenderPreviewMatrix4x4.Identity;
        Camera.RadialTransitionMeters=0f;
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
        Camera.ViewProjection.Values[15]=0f;
        Assert.Contains("projection",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);
        Request.ProtocolVersion=8;
        Assert.Contains("Unsupported",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_ValidatesPresentationModeSelectionWeightAndCameraPose(){
        RenderPreviewHostRequest Request=CreateCommand(
            RenderPreviewHostCommand.UpdateFrame);
        Request.Frame=new RenderPreviewFrame {
            Camera=new RenderPreviewCamera {
                ViewProjection=RenderPreviewMatrix4x4.Identity,
            },
            PresentationMode=RenderPreviewPresentationMode.Wireframe,
            Objects=[
                new RenderPreviewObjectTransform {
                    ObjectId="Root/A",
                    World=RenderPreviewMatrix4x4.Identity,
                    SelectionWeight=0.5f,
                },
            ],
        };

        Assert.Null(RenderPreviewHostProtocol.Validate(Request));

        Request.Frame.Objects[0].SelectionWeight=float.NaN;
        Assert.Contains("selection",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);

        Request.Frame.Objects[0].SelectionWeight=0f;
        Request.Frame.PresentationMode=(RenderPreviewPresentationMode)255;
        Assert.Contains("presentation",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);

        // Protocol 6 (ADR-0070 B1): the runtime view's camera carries its rotation and projection.
        Request.Frame.PresentationMode=RenderPreviewPresentationMode.Wireframe;
        Request.Frame.Camera.Rotation=[0f,0f,0f];
        Assert.Contains("rotation",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
        Request.Frame.Camera.Rotation=[0f,0f,0f,1f];
        Request.Frame.Camera.NearZ=10f;
        Request.Frame.Camera.FarZ=1f;
        Assert.Contains("projection",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtocolV5_ValidatesBoundedCompleteOverlayTopology(){
        RenderPreviewHostRequest Request=CreateCommand(
            RenderPreviewHostCommand.UpdateFrame);
        Request.Frame=new RenderPreviewFrame {
            Camera=new RenderPreviewCamera {
                ViewProjection=RenderPreviewMatrix4x4.Identity,
            },
            OverlayTriangles=[
                OverlayVertex(-0.5f,-0.5f,0f),
                OverlayVertex(0.5f,-0.5f,0f),
                OverlayVertex(0f,0.5f,0f),
            ],
        };

        Assert.Null(RenderPreviewHostProtocol.Validate(Request));

        Request.Frame.OverlayLines=[OverlayVertex(0f,0f,0f)];
        Assert.Contains("line",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);

        Request.Frame.OverlayLines=[];
        Request.Frame.OverlayTriangles[0].Color[3]=float.PositiveInfinity;
        Assert.Contains("finite",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtocolV5_ValidatesBoundedCameraPreviewPasses(){
        RenderPreviewHostRequest Request=CreateCommand(
            RenderPreviewHostCommand.UpdateFrame);
        Request.Frame=new RenderPreviewFrame {
            Camera=new RenderPreviewCamera {
                ViewProjection=RenderPreviewMatrix4x4.Identity,
                Position=[0f,0f,0f],
            },
            Objects=[
                new RenderPreviewObjectTransform {
                    ObjectId="Root/Camera",
                    World=RenderPreviewMatrix4x4.Identity,
                },
            ],
            CameraPreviews=[
                new RenderPreviewCameraPass {
                    X=-16,Y=8,Width=64,Height=36,
                    Camera=new RenderPreviewCamera {
                        ViewProjection=RenderPreviewMatrix4x4.Identity,
                        Position=[1f,2f,3f],
                    },
                    ExcludedObjectId="Root/Camera",
                },
            ],
        };

        Assert.Null(RenderPreviewHostProtocol.Validate(Request));

        Request.Frame.CameraPreviews[0].Camera!.Position[1]=float.NaN;
        Assert.Contains("position",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);

        Request.Frame.CameraPreviews[0].Camera!.Position=[1f,2f,3f];
        Request.Frame.CameraPreviews[0].Width=0;
        Assert.Contains("dimensions",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);

        Request.Frame.CameraPreviews=[
            Request.Frame.CameraPreviews[0],
            new RenderPreviewCameraPass(),new RenderPreviewCameraPass(),
            new RenderPreviewCameraPass(),
        ];
        Assert.Contains("count",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_ValidSnapshot_IsAccepted(){
        RenderPreviewHostRequest Request=CreateOpenRequest();

        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
    }

    [Fact]
    public void VersionMismatch_IsRejectedBeforePayload(){
        RenderPreviewHostRequest Request=CreateOpenRequest();
        Request.ProtocolVersion++;

        string Error=Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request));

        Assert.Contains("Unsupported",Error,StringComparison.Ordinal);
    }

    [Fact]
    public void Open_RejectsInvalidTopologyAndNonFiniteState(){
        RenderPreviewHostRequest Request=CreateOpenRequest();
        Request.Scene!.Meshes[0].Indices=[0,1,3];
        Assert.Contains("index",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);

        Request=CreateOpenRequest();
        Request.Scene!.Camera!.ViewProjection.Values[7]=float.NaN;
        Assert.Contains("finite",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);

        Request=CreateOpenRequest();
        Request.Scene!.Objects[0].MaterialIndex=1;
        Assert.Contains("material",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandPayloads_AreExact(){
        RenderPreviewHostRequest Request=CreateOpenRequest();
        Request.Command=RenderPreviewHostCommand.Render;
        Assert.Contains("must not include",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);

        Request=CreateCommand(RenderPreviewHostCommand.Resize);
        Request.Width=0;
        Request.Height=0;
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));

        Request=CreateCommand(RenderPreviewHostCommand.Resize);
        Request.Width=32;
        Request.Height=0;
        Assert.Contains("both",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);

        Request=CreateCommand(RenderPreviewHostCommand.ReplaceScene);
        Assert.Contains("requires",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.OrdinalIgnoreCase);

        Request=CreateCommand(RenderPreviewHostCommand.Close);
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
        Request.Width=1;
        Assert.Contains("must not include",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdentityAndOrdering_AreMandatory(){
        RenderPreviewHostRequest Request=CreateOpenRequest();
        Request.RequestId=Guid.Empty;
        Assert.Contains("RequestId",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.Ordinal);

        Request=CreateOpenRequest();
        Request.SessionId=Guid.Empty;
        Assert.Contains("SessionId",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.Ordinal);

        Request=CreateOpenRequest();
        Request.Sequence=0;
        Assert.Contains("Sequence",Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFrame_RequiresUniqueKnownShapeAndFiniteMatrices(){
        RenderPreviewHostRequest Request=CreateCommand(RenderPreviewHostCommand.UpdateFrame);
        Request.Frame=new RenderPreviewFrame {
            Camera=new RenderPreviewCamera { ViewProjection=RenderPreviewMatrix4x4.Identity },
            Objects=[
                new RenderPreviewObjectTransform {
                    ObjectId="Root/A",
                    World=RenderPreviewMatrix4x4.Identity,
                },
                new RenderPreviewObjectTransform {
                    ObjectId="Root/B",
                    World=RenderPreviewMatrix4x4.Identity,
                },
            ],
        };
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));

        Request.Frame.Objects[1].ObjectId="Root/A";
        Assert.Contains("duplicated",
            Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);

        Request.Frame.Objects[1].ObjectId="Root/B";
        Request.Frame.Objects[1].World.Values[0]=float.PositiveInfinity;
        Assert.Contains("finite",
            Assert.IsType<string>(RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyAuthoringSceneAndFrame_AreAccepted(){
        RenderPreviewHostRequest Request=CreateOpenRequest();
        Request.Scene!.Meshes=[];
        Request.Scene.Materials=[];
        Request.Scene.Objects=[];

        Assert.Null(RenderPreviewHostProtocol.Validate(Request));

        Request=CreateCommand(RenderPreviewHostCommand.UpdateFrame);
        Request.Frame=new RenderPreviewFrame {
            Camera=new RenderPreviewCamera {
                ViewProjection=RenderPreviewMatrix4x4.Identity,
            },
            Objects=[],
        };

        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
    }

    internal static RenderPreviewHostRequest CreateOpenRequest()=>new(){
        RequestId=Guid.NewGuid(),
        SessionId=Guid.NewGuid(),
        Sequence=1,
        Command=RenderPreviewHostCommand.Open,
        SurfaceHandle=123,
        Width=64,
        Height=48,
        ShaderArtifactPath="Stage.cgeshaderartifact",
        Scene=new RenderPreviewScene {
            Camera=new RenderPreviewCamera {
                ViewProjection=RenderPreviewMatrix4x4.Identity,
            },
            Meshes=[
                new RenderPreviewMesh {
                    Positions=[-0.5f,-0.5f,0f,0.5f,-0.5f,0f,0f,0.5f,0f],
                    Normals=[0f,0f,-1f,0f,0f,-1f,0f,0f,-1f],
                    Indices=[0,1,2],
                },
            ],
            Materials=[
                new RenderPreviewMaterial { BaseColor=[1f,0f,0f,1f] },
            ],
            Objects=[
                new RenderPreviewObject {
                    ObjectId="Root/Triangle",
                    MeshIndex=0,
                    MaterialIndex=0,
                    World=RenderPreviewMatrix4x4.Identity,
                },
            ],
        },
    };

    private static RenderPreviewHostRequest CreateCommand(RenderPreviewHostCommand Command)=>new(){
        RequestId=Guid.NewGuid(),
        SessionId=Guid.NewGuid(),
        Sequence=2,
        Command=Command,
    };

    private static RenderPreviewOverlayVertex OverlayVertex(float X,float Y,float Z)=>new(){
        Position=[X,Y,Z],
        Color=[1f,0.5f,0.25f,1f],
    };
}
