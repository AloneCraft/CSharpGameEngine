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
        Client.Close();

        Assert.Equal(
            new[]{
                RenderPreviewHostCommand.Open,
                RenderPreviewHostCommand.Resize,
                RenderPreviewHostCommand.ReplaceScene,
                RenderPreviewHostCommand.Render,
                RenderPreviewHostCommand.Close,
            },
            Requests.ConvertAll(static Request=>Request.Command));
        Assert.Equal(new ulong[]{1,2,3,4,5},Requests.ConvertAll(static Request=>Request.Sequence));
        Assert.All(Requests,Request=>Assert.Equal(SessionId,Request.SessionId));
        Assert.True(Requests[3].CaptureForVerification);
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

    private static RenderPreviewHostResponse Success(RenderPreviewHostRequest Request)=>new(){
        RequestId=Request.RequestId,
        SessionId=Request.SessionId,
        Sequence=Request.Sequence,
        State=RenderPreviewHostResultState.Succeeded,
        DeviceGeneration=1,
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
