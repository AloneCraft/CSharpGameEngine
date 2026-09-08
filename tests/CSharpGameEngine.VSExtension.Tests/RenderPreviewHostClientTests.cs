using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.VSExtension;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class RenderPreviewHostClientTests {
    [Fact]
    public void ProcessStartInfo_UsesDotnetForDllAndDirectExecutionForAppHost(){
        string Root=Path.Combine(Path.GetTempPath(),$"CgePreviewStart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        string DllPath=Path.Combine(Root,"Preview Host.dll");
        string ExePath=Path.Combine(Root,"Preview Host.exe");
        File.WriteAllBytes(DllPath,[1]);
        File.WriteAllBytes(ExePath,[2]);
        try {
            ProcessStartInfo Dll=PreviewHostProcessStartInfo.Create(
                DllPath,"--render-server test",Root,ParentProcessId:4242);
            ProcessStartInfo Exe=PreviewHostProcessStartInfo.Create(
                ExePath,"--render-server test",Root,ParentProcessId:4242);

            Assert.Equal("dotnet",Dll.FileName);
            Assert.StartsWith($"\"{Path.GetFullPath(DllPath)}\" ",Dll.Arguments);
            Assert.Equal(Path.GetFullPath(ExePath),Exe.FileName);
            Assert.Equal("--render-server test --parent-process-id 4242",Exe.Arguments);
            Assert.Contains("--parent-process-id 4242",Dll.Arguments,StringComparison.Ordinal);
            Assert.Equal(Root,Dll.WorkingDirectory);
            Assert.Equal(Root,Exe.WorkingDirectory);
            Assert.False(Dll.UseShellExecute);
            Assert.True(Dll.CreateNoWindow);
        } finally {
            Directory.Delete(Root,recursive:true);
        }
    }

    [Fact]
    public void ProcessStartInfo_RejectsInvalidOwnerProcessId(){
        string HostPath=Path.Combine(Path.GetTempPath(),$"CgePreviewHost-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(HostPath,[1]);
        try {
            Assert.Throws<ArgumentOutOfRangeException>(()=>
                PreviewHostProcessStartInfo.Create(
                    HostPath,"--render-server test",ParentProcessId:0));
        } finally {
            File.Delete(HostPath);
        }
    }

    [Fact]
    public void CommandsUseOneSessionAndStrictMonotonicSequence(){
        List<RenderPreviewHostRequest>Requests=[];
        Guid SessionId=Guid.NewGuid();
        using RenderPreviewHostClient Client=new(
            SessionId,
            Request=>{
                Requests.Add(Request);
                return Success(Request);
            },
            ()=>{});

        Client.Open(123,64,48,"Stage.cgeshaderartifact",Scene());
        Client.Resize(32,24);
        Client.ReplaceScene(Scene());
        Client.Render(captureForVerification:true);
        RenderPreviewDebugSnapshot Snapshot=Client.GetDebugSnapshot();
        Client.Close();

        Assert.Equal(
            new[]{
                RenderPreviewHostCommand.Open,
                RenderPreviewHostCommand.Resize,
                RenderPreviewHostCommand.ReplaceScene,
                RenderPreviewHostCommand.Render,
                RenderPreviewHostCommand.Snapshot,
                RenderPreviewHostCommand.Close,
            },
            Requests.ConvertAll(static Request=>Request.Command));
        Assert.Equal(new ulong[]{1,2,3,4,5,6},Requests.ConvertAll(static Request=>Request.Sequence));
        Assert.All(Requests,Request=>Assert.Equal(SessionId,Request.SessionId));
        Assert.True(Requests[3].CaptureForVerification);
        Assert.Equal(4u,Snapshot.FramesRendered);
        Assert.Equal(RenderPreviewHostClientState.Disposed,Client.State);
    }

    [Fact]
    public void TransportCrashIsTerminalAndRequiresFreshSurfaceGeneration(){
        int Calls=0;
        using RenderPreviewHostClient Client=new(
            Guid.NewGuid(),
            Request=>{
                Calls++;
                if(Request.Command==RenderPreviewHostCommand.Render){
                    throw new IOException("host exited");
                }
                return Success(Request);
            },
            ()=>{});
        Client.Open(123,64,48,"Stage.cgeshaderartifact",Scene());

        RenderPreviewHostException Error=Assert.Throws<RenderPreviewHostException>(
            ()=>{ Client.Render(captureForVerification:false); });

        Assert.True(Error.RequiresFreshSurfaceGeneration);
        Assert.Equal(RenderPreviewHostClientState.Faulted,Client.State);
        Assert.Throws<InvalidOperationException>(()=>{ Client.Resize(20,20); });
        Assert.Equal(2,Calls);
    }

    [Fact]
    public void SnapshotCaptureFailure_IsNonTerminalAndConsumesItsSequence(){
        List<RenderPreviewHostRequest>Requests=[];
        using RenderPreviewHostClient Client=new(
            Guid.NewGuid(),
            Request=>{
                Requests.Add(Request);
                if(Request.Command==RenderPreviewHostCommand.Snapshot){
                    return new RenderPreviewHostResponse {
                        RequestId=Request.RequestId,SessionId=Request.SessionId,
                        Sequence=Request.Sequence,State=RenderPreviewHostResultState.Failed,
                        DiagnosticCode="PREVIEW_SNAPSHOT_FAILED",Message="capture fault",
                    };
                }
                return Success(Request);
            },
            ()=>{});
        Client.Open(123,64,48,"Stage.cgeshaderartifact",Scene());

        RenderPreviewHostException Error=Assert.Throws<RenderPreviewHostException>(
            ()=>Client.GetDebugSnapshot());

        Assert.False(Error.SessionTerminated);
        Assert.False(Error.RequiresFreshSurfaceGeneration);
        Assert.Equal(RenderPreviewHostClientState.Ready,Client.State);
        Client.Render(captureForVerification:false);
        Assert.Equal(new ulong[]{1,2,3},
            Requests.ConvertAll(static Request=>Request.Sequence));
    }

    [Fact]
    public void MalformedSuccessfulSnapshot_IsTerminalProtocolFailure(){
        using RenderPreviewHostClient Client=new(
            Guid.NewGuid(),
            Request=>new RenderPreviewHostResponse {
                RequestId=Request.RequestId,SessionId=Request.SessionId,
                Sequence=Request.Sequence,State=RenderPreviewHostResultState.Succeeded,
                DebugSnapshot=null,
            },
            ()=>{});
        Client.Open(123,64,48,"Stage.cgeshaderartifact",Scene());

        RenderPreviewHostException Error=Assert.Throws<RenderPreviewHostException>(
            ()=>Client.GetDebugSnapshot());

        Assert.True(Error.SessionTerminated);
        Assert.True(Error.RequiresFreshSurfaceGeneration);
        Assert.Equal(RenderPreviewHostClientState.Faulted,Client.State);
    }

    [Fact]
    public void SnapshotEnvelopeMismatch_IsTerminalProtocolFailure(){
        using RenderPreviewHostClient Client=new(
            Guid.NewGuid(),
            Request=>new RenderPreviewHostResponse {
                RequestId=Request.RequestId,SessionId=Request.SessionId,
                Sequence=Request.Sequence,State=RenderPreviewHostResultState.Succeeded,
                DeviceGeneration=1,CompletedFence=8,
                DebugSnapshot=Request.Command==RenderPreviewHostCommand.Snapshot
                    ?DebugSnapshot():null,
            },
            ()=>{});
        Client.Open(123,64,48,"Stage.cgeshaderartifact",Scene());

        RenderPreviewHostException Error=Assert.Throws<RenderPreviewHostException>(
            ()=>Client.GetDebugSnapshot());

        Assert.True(Error.SessionTerminated);
        Assert.Contains("generation or fence",Error.Message,StringComparison.Ordinal);
        Assert.Equal(RenderPreviewHostClientState.Faulted,Client.State);
    }

    private static RenderPreviewHostResponse Success(RenderPreviewHostRequest Request)=>new(){
        RequestId=Request.RequestId,
        SessionId=Request.SessionId,
        Sequence=Request.Sequence,
        State=RenderPreviewHostResultState.Succeeded,
        DeviceGeneration=1,
        CompletedFence=Request.Command==RenderPreviewHostCommand.Snapshot?9ul:0ul,
        DebugSnapshot=Request.Command==RenderPreviewHostCommand.Snapshot
            ?DebugSnapshot():null,
    };

    private static RenderPreviewDebugSnapshot DebugSnapshot()=>new(){
        DeviceState=RenderPreviewDebugDeviceState.Ready,
        DeviceGeneration=1,
        CompletedFence=9,
        FramesRendered=4,
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
        Platform=new PlatformDebugSnapshot {
            CapturedCounter=123,CounterFrequency=10_000_000,
            OsDescription="Windows",OsArchitecture="X64",
            ProcessArchitecture="X64",RuntimeDescription=".NET 10",ProcessId=42,
            Input=new PlatformDebugInputSnapshot {
                State=PlatformDebugSubsystemState.NotOwned,
                Detail="The test host owns no input.",
            },
            Audio=new PlatformDebugAudioSnapshot {
                State=PlatformDebugSubsystemState.NotOwned,
                Mode=PlatformDebugAudioBackendMode.NotOwned,
                Detail="The test host owns no audio.",
            },
        },
    };

    private static RenderPreviewScene Scene()=>new(){
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
    };
}
