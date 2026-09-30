using CSharpGameEngine.Contracts;
using CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class RenderPreviewHostSessionTableTests {
    [Fact]
    public void OpenRenderClose_UsesOneOwnedSessionAndStrictSequence(){
        FakeSession?Owned=null;
        using RenderPreviewHostSessionTable Table=new(_=>Owned=new FakeSession());
        RenderPreviewHostRequest Open=CreateOpen();

        RenderPreviewHostResponse Opened=Table.Handle(Open);
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Opened.State);
        Assert.NotNull(Owned);

        RenderPreviewHostRequest Render=Command(Open,RenderPreviewHostCommand.Render,2);
        RenderPreviewHostResponse Rendered=Table.Handle(Render);
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Rendered.State);
        Assert.Equal(1,Owned.RenderCalls);
        RenderPreviewCameraDiagnostics Camera=Assert.Single(Rendered.CameraPreviews);
        Assert.Equal(3,Camera.SceneObjectCount);
        Assert.Equal(2,Camera.DrawCallCount);

        RenderPreviewHostResponse Stale=Table.Handle(Render);
        Assert.Equal(RenderPreviewHostResultState.StaleSequence,Stale.State);
        Assert.Equal(1,Owned.RenderCalls);

        RenderPreviewHostResponse Closed=Table.Handle(Command(Open,RenderPreviewHostCommand.Close,3));
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Closed.State);
        Assert.True(Owned.Disposed);
        Assert.Equal(RenderPreviewHostResultState.SessionNotFound,
            Table.Handle(Command(Open,RenderPreviewHostCommand.Render,4)).State);
    }

    [Fact]
    public void InvalidOrMismatchedOpen_NeverCreatesSession(){
        int FactoryCalls=0;
        using RenderPreviewHostSessionTable Table=new(_=>{ FactoryCalls++;return new FakeSession(); });
        RenderPreviewHostRequest Invalid=CreateOpen();
        Invalid.ProtocolVersion++;

        RenderPreviewHostResponse Mismatch=Table.Handle(Invalid);

        Assert.Equal(RenderPreviewHostResultState.VersionMismatch,Mismatch.State);
        Assert.Equal(0,FactoryCalls);
    }

    [Fact]
    public void CommandFailure_IsTerminalForThatSession(){
        FakeSession?Owned=null;
        using RenderPreviewHostSessionTable Table=new(_=>Owned=new FakeSession { ThrowOnReplace=true });
        RenderPreviewHostRequest Open=CreateOpen();
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Open).State);
        RenderPreviewHostRequest Replace=Command(Open,RenderPreviewHostCommand.ReplaceScene,2);
        Replace.Scene=Open.Scene;

        RenderPreviewHostResponse Failed=Table.Handle(Replace);

        Assert.Equal(RenderPreviewHostResultState.Failed,Failed.State);
        Assert.Equal("PREVIEW_COMMAND_FAILED",Failed.DiagnosticCode);
        Assert.True(Owned!.Disposed);
        Assert.Equal(RenderPreviewHostResultState.SessionNotFound,
            Table.Handle(Command(Open,RenderPreviewHostCommand.Render,3)).State);
    }

    [Fact]
    public void Snapshot_IsReadOnlyAndItsCaptureFailureDoesNotDisposeSession(){
        FakeSession?Owned=null;
        using RenderPreviewHostSessionTable Table=new(_=>Owned=new FakeSession());
        RenderPreviewHostRequest Open=CreateOpen();
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Open).State);

        RenderPreviewHostResponse Captured=Table.Handle(
            Command(Open,RenderPreviewHostCommand.Snapshot,2));

        Assert.Equal(RenderPreviewHostResultState.Succeeded,Captured.State);
        Assert.NotNull(Captured.DebugSnapshot);
        Assert.Null(RenderPreviewHostProtocol.ValidateDebugSnapshot(
            Captured.DebugSnapshot));
        Assert.Equal(1,Owned!.SnapshotCalls);
        Assert.Equal(0,Owned.RenderCalls);
        Assert.False(Owned.Disposed);

        Owned.ThrowOnSnapshot=true;
        RenderPreviewHostResponse Failed=Table.Handle(
            Command(Open,RenderPreviewHostCommand.Snapshot,3));

        Assert.Equal(RenderPreviewHostResultState.Failed,Failed.State);
        Assert.Equal("PREVIEW_SNAPSHOT_FAILED",Failed.DiagnosticCode);
        Assert.False(Owned.Disposed);
        Assert.Equal(RenderPreviewHostResultState.Succeeded,
            Table.Handle(Command(Open,RenderPreviewHostCommand.Render,4)).State);
        Assert.Equal(1,Owned.RenderCalls);
    }

    [Fact]
    public void MalformedSnapshot_FailsClosedWithoutDisposingSession(){
        FakeSession?Owned=null;
        using RenderPreviewHostSessionTable Table=new(_=>Owned=new FakeSession());
        RenderPreviewHostRequest Open=CreateOpen();
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Open).State);
        Owned!.ReturnMalformedSnapshot=true;

        RenderPreviewHostResponse Failed=Table.Handle(
            Command(Open,RenderPreviewHostCommand.Snapshot,2));

        Assert.Equal(RenderPreviewHostResultState.Failed,Failed.State);
        Assert.Equal("PREVIEW_SNAPSHOT_FAILED",Failed.DiagnosticCode);
        Assert.Contains("invalid debug snapshot",Failed.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Owned.Disposed);
        Assert.Equal(RenderPreviewHostResultState.Succeeded,
            Table.Handle(Command(Open,RenderPreviewHostCommand.Render,3)).State);
    }

    internal static RenderPreviewHostRequest CreateOpenForPipe()=>CreateOpen();

    private static RenderPreviewHostRequest CreateOpen()=>new(){
        RequestId=Guid.NewGuid(),
        SessionId=Guid.NewGuid(),
        Sequence=1,
        Command=RenderPreviewHostCommand.Open,
        SurfaceHandle=1,
        Width=64,
        Height=64,
        ShaderArtifactPath="Stage.cgeshaderartifact",
        Scene=new RenderPreviewScene {
            Camera=new RenderPreviewCamera { ViewProjection=RenderPreviewMatrix4x4.Identity },
            Meshes=[new RenderPreviewMesh {
                Positions=[0f,0f,0f,1f,0f,0f,0f,1f,0f],
                Normals=[0f,0f,-1f,0f,0f,-1f,0f,0f,-1f],
                Indices=[0,1,2],
            }],
            Materials=[new RenderPreviewMaterial()],
            Objects=[new RenderPreviewObject {
                ObjectId="Triangle",
                World=RenderPreviewMatrix4x4.Identity,
            }],
        },
    };

    private static RenderPreviewHostRequest Command(
        RenderPreviewHostRequest Open,RenderPreviewHostCommand Command,ulong Sequence)=>new(){
            RequestId=Guid.NewGuid(),
            SessionId=Open.SessionId,
            Sequence=Sequence,
            Command=Command,
        };

    private sealed class FakeSession:IRenderPreviewSession {
        public uint DeviceGeneration=>7;
        public ulong CompletedFence=>11;
        public string LastFrameHash=>string.Empty;
        public RenderPreviewCameraDiagnostics[]CameraPreviewDiagnostics{get;}=[
            new(){ SceneObjectCount=3,FrustumCandidateCount=3,DrawCallCount=2,ViewProjectionHash=17 },
        ];
        public int RenderCalls{get;private set;}
        public int SnapshotCalls{get;private set;}
        public bool Disposed{get;private set;}
        public bool ThrowOnReplace{get;init;}
        public bool ThrowOnSnapshot{get;set;}
        public bool ReturnMalformedSnapshot{get;set;}

        public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){
            if(this.ThrowOnReplace){ throw new InvalidOperationException("fault"); }
        }
        public void UpdateFrame(RenderPreviewFrame Frame){ }
        public void Resize(int Width,int Height){ }
        public void Render(bool CaptureForVerification){ this.RenderCalls++; }
        public RenderPreviewDebugSnapshot CaptureDebugSnapshot(){
            this.SnapshotCalls++;
            if(this.ThrowOnSnapshot){ throw new InvalidOperationException("snapshot fault"); }
            if(this.ReturnMalformedSnapshot){ return new RenderPreviewDebugSnapshot(); }
            return new RenderPreviewDebugSnapshot {
                DeviceState=RenderPreviewDebugDeviceState.Ready,
                DeviceGeneration=7,CompletedFence=11,
                SurfaceWidth=64,SurfaceHeight=64,
                AdapterDescription="Fake",QualityProfile="Compatibility",
                Passes=[new RenderPreviewDebugPass {
                    Index=0,Name="Present",Queue=RenderPreviewDebugQueue.Graphics,
                    ExecutionIndex=0,
                }],
                Resources=[new RenderPreviewDebugResource {
                    Index=0,Name="BackBuffer",
                    Kind=RenderPreviewDebugResourceKind.Texture,
                    Width=64,Height=64,Format="rgba8",Imported=true,
                    FirstUse=0,LastUse=0,AliasSlot=-1,
                }],
                Platform=PlatformDebugSnapshotCapture.Capture(
                    PlatformDebugSnapshotCapture.NotOwnedInput(
                        "Test session owns no input."),
                    PlatformDebugSnapshotCapture.NotOwnedAudio(
                        "Test session owns no audio.")),
            };
        }
        public void Dispose(){ this.Disposed=true; }
    }
}
