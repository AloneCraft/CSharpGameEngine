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
        public bool Disposed{get;private set;}
        public bool ThrowOnReplace{get;init;}

        public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){
            if(this.ThrowOnReplace){ throw new InvalidOperationException("fault"); }
        }
        public void UpdateFrame(RenderPreviewFrame Frame){ }
        public void Resize(int Width,int Height){ }
        public void Render(bool CaptureForVerification){ this.RenderCalls++; }
        public void Dispose(){ this.Disposed=true; }
    }
}
