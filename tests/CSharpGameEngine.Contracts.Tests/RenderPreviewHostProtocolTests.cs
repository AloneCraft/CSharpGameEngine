using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Contracts.Tests;

public sealed class RenderPreviewHostProtocolTests {
    [Fact]
    public void ProtocolV5_ValidatesPresentationModeAndSelectionWeight(){
        Assert.Equal(5,RenderPreviewHostProtocol.CurrentVersion);
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

        Request=CreateCommand(RenderPreviewHostCommand.Snapshot);
        Assert.Null(RenderPreviewHostProtocol.Validate(Request));
        Request.Width=1;
        Assert.Contains("must not include",Assert.IsType<string>(
            RenderPreviewHostProtocol.Validate(Request)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DebugSnapshot_ValidatesVersionBoundsTopologyAndDiagnostics(){
        RenderPreviewDebugSnapshot Snapshot=CreateDebugSnapshot();

        Assert.Null(RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot));

        Snapshot.SnapshotVersion++;
        Assert.Contains("Unsupported",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot)),
            StringComparison.Ordinal);
        Snapshot.SnapshotVersion=RenderPreviewHostProtocol.CurrentDebugSnapshotVersion;

        Snapshot.Passes[0].Dependencies=[0];
        Assert.Contains("dependency",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot)),
            StringComparison.OrdinalIgnoreCase);
        Snapshot.Passes[0].Dependencies=[];

        Snapshot.Resources[0].AliasSlot=0;
        Assert.Contains("lifetime",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot)),
            StringComparison.OrdinalIgnoreCase);
        Snapshot.Resources[0].AliasSlot=-1;

        Snapshot.Diagnostics[0].Description=new string(
            'x',RenderPreviewHostProtocol.MaximumDebugDiagnosticDescriptionLength+1);
        Assert.Contains("description",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot=CreateDebugSnapshot();
        Snapshot.Resources[0].Format=null!;
        Assert.Contains("descriptor",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot=CreateDebugSnapshot();
        Snapshot.DiagnosticsTruncated=true;
        Assert.Contains("capacity",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlatformDebugSnapshot_RejectsMalformedOwnedAndNotOwnedState(){
        RenderPreviewDebugSnapshot Snapshot=CreateDebugSnapshot();
        Assert.Equal(2,Snapshot.SnapshotVersion);
        Assert.Null(RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(
            Snapshot.Platform));

        Snapshot.Platform.CounterFrequency=0;
        Assert.Contains("counter",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(Snapshot.Platform)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot=CreateDebugSnapshot();
        Snapshot.Platform.Input.Actions[0].ValueX=float.NaN;
        Assert.Contains("invalid",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(Snapshot.Platform)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot=CreateDebugSnapshot();
        Snapshot.Platform.Input.State=PlatformDebugSubsystemState.NotOwned;
        Assert.Contains("not-owned",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(Snapshot.Platform)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot=CreateDebugSnapshot();
        Snapshot.Platform.Audio.Mode=PlatformDebugAudioBackendMode.NotOwned;
        Assert.Contains("owned",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(Snapshot.Platform)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot=CreateDebugSnapshot();
        Snapshot.Platform.Input=new PlatformDebugInputSnapshot {
            State=PlatformDebugSubsystemState.NotOwned,
            ActionsTruncated=true,
        };
        Snapshot.Platform.Audio=new PlatformDebugAudioSnapshot {
            State=PlatformDebugSubsystemState.NotOwned,
            Mode=PlatformDebugAudioBackendMode.NotOwned,
        };
        Assert.Contains("not-owned",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(Snapshot.Platform)),
            StringComparison.OrdinalIgnoreCase);

        Snapshot.Platform.Input.ActionsTruncated=false;
        Snapshot.Platform.Audio.LastFailure="fabricated failure";
        Assert.Contains("not-owned",Assert.IsType<string>(
            RenderPreviewHostProtocol.ValidatePlatformDebugSnapshot(Snapshot.Platform)),
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

    internal static RenderPreviewDebugSnapshot CreateDebugSnapshot()=>new(){
        GraphKind=RenderPreviewDebugGraphKind.DeclaredPlan,
        DeviceState=RenderPreviewDebugDeviceState.Ready,
        DeviceGeneration=2,
        CompletedFence=8,
        FramesRendered=3,
        SurfaceWidth=64,
        SurfaceHeight=48,
        SceneObjectCount=1,
        PrimaryObjectDrawCount=1,
        ResidentMaterialTextureCount=1,
        AdapterDescription="WARP",
        IsWarp=true,
        QualityProfile="Compatibility",
        Passes=[new RenderPreviewDebugPass {
            Index=0,Name="Present",Queue=RenderPreviewDebugQueue.Graphics,
            ExecutionIndex=0,
        }],
        Resources=[new RenderPreviewDebugResource {
            Index=0,Name="BackBuffer",Kind=RenderPreviewDebugResourceKind.Texture,
            Width=64,Height=48,Format="rgba8",Imported=true,
            FirstUse=0,LastUse=0,AliasSlot=-1,
        }],
        Transitions=[new RenderPreviewDebugTransition {
            ResourceIndex=0,PassIndex=0,
            Before=RenderPreviewDebugResourceState.External,
            After=RenderPreviewDebugResourceState.Write,
            SourceQueue=RenderPreviewDebugQueue.Graphics,
            DestinationQueue=RenderPreviewDebugQueue.Graphics,
        }],
        Diagnostics=[new RenderPreviewDebugDiagnostic {
            Severity=RenderPreviewDebugSeverity.Warning,
            Category="Execution",Id="Example",Description="Bounded message",
        }],
        Platform=new PlatformDebugSnapshot {
            CapturedCounter=123,
            CounterFrequency=10_000_000,
            OsDescription="Windows",
            OsArchitecture="X64",
            ProcessArchitecture="X64",
            RuntimeDescription=".NET 10",
            ProcessId=42,
            Input=new PlatformDebugInputSnapshot {
                State=PlatformDebugSubsystemState.Ready,
                ActiveContextCount=1,
                HeldControlCount=1,
                ActiveActionCount=1,
                Actions=[new PlatformDebugInputAction {
                    TimestampTicks=120,
                    WindowId="window",
                    DeviceId="keyboard",
                    Sequence=4,
                    ContextId="gameplay",
                    ActionId="jump",
                    Phase=PlatformDebugInputActionPhase.Started,
                    RawX=1f,
                    ValueX=1f,
                }],
            },
            Audio=new PlatformDebugAudioSnapshot {
                State=PlatformDebugSubsystemState.Ready,
                Mode=PlatformDebugAudioBackendMode.Silent,
                Generation=1,
                MaximumVoiceCount=128,
                BusCount=1,
            },
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
