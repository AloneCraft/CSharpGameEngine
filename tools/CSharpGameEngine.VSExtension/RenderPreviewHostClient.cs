using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

internal enum RenderPreviewHostClientState:byte {
    Created,
    Ready,
    Faulted,
    Disposed,
}

internal sealed class RenderPreviewHostException:Exception {
    internal RenderPreviewHostException(
        string Message,bool RequiresFreshSurfaceGeneration,Exception?InnerException=null,
        bool SessionTerminated=true)
        :base(Message,InnerException){
        this.RequiresFreshSurfaceGeneration=RequiresFreshSurfaceGeneration;
        this.SessionTerminated=SessionTerminated;
    }

    internal bool RequiresFreshSurfaceGeneration{get;}
    internal bool SessionTerminated{get;}
}

/// <summary>
/// VSIX-side state source for one isolated Render Server session. Transport failures are terminal
/// because an abruptly terminated DXGI owner can leave the old HWND unusable for a new swap chain;
/// recovery creates both a fresh client and a fresh surface generation.
/// </summary>
internal sealed class RenderPreviewHostClient:IDisposable {
    private readonly Guid SessionId;
    private readonly Func<RenderPreviewHostRequest,RenderPreviewHostResponse>SendRequest;
    private readonly Action DisposeTransport;
    private ulong Sequence;
    private bool TransportDisposed;

    internal RenderPreviewHostClient(
        Guid SessionId,
        Func<RenderPreviewHostRequest,RenderPreviewHostResponse>SendRequest,
        Action DisposeTransport){
        if(SessionId==Guid.Empty){ throw new ArgumentException("Session ID is required.",nameof(SessionId)); }
        this.SessionId=SessionId;
        this.SendRequest=SendRequest??throw new ArgumentNullException(nameof(SendRequest));
        this.DisposeTransport=DisposeTransport??throw new ArgumentNullException(nameof(DisposeTransport));
    }

    internal RenderPreviewHostClientState State{get;private set;}=RenderPreviewHostClientState.Created;

    /// <param name="ResponseTimeout">
    /// How long one request may take; a runtime view (ADR-0070 B1) loads the game's level, which takes
    /// as long as the game's own startup. 15 seconds when omitted.
    /// </param>
    internal static RenderPreviewHostClient Start(string HostPath,TimeSpan?ResponseTimeout=null){
        RenderPreviewHostProcessTransport Transport=new(HostPath,ResponseTimeout??TimeSpan.FromSeconds(15));
        try {
            return new RenderPreviewHostClient(
                Guid.NewGuid(),Transport.Send,Transport.Dispose);
        } catch {
            Transport.Dispose();
            throw;
        }
    }

    /// <param name="RuntimeView">ADR-0070 B1: render the built game's world instead of <paramref name="Scene"/>.</param>
    /// <param name="SceneJson">The edited scene's document, whose values a runtime view applies.</param>
    internal RenderPreviewHostResponse Open(
        long SurfaceHandle,int Width,int Height,string ShaderArtifactPath,RenderPreviewScene Scene,
        RenderPreviewRuntimeView?RuntimeView=null,string SceneJson=""){
        if(this.State!=RenderPreviewHostClientState.Created){
            throw new InvalidOperationException("Preview client can only be opened once.");
        }
        RenderPreviewHostResponse Response=this.Send(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=this.SessionId,
            Sequence=++this.Sequence,
            Command=RenderPreviewHostCommand.Open,
            SurfaceHandle=SurfaceHandle,
            Width=Width,
            Height=Height,
            ShaderArtifactPath=ShaderArtifactPath??string.Empty,
            Scene=Scene,
            RuntimeView=RuntimeView,
            SceneJson=SceneJson??string.Empty,
        });
        this.State=RenderPreviewHostClientState.Ready;
        return Response;
    }

    internal RenderPreviewHostResponse ReplaceScene(RenderPreviewScene Scene,string SceneJson=""){
        this.EnsureReady();
        return this.Send(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=this.SessionId,
            Sequence=++this.Sequence,
            Command=RenderPreviewHostCommand.ReplaceScene,
            Scene=Scene,
            SceneJson=SceneJson??string.Empty,
        });
    }

    internal RenderPreviewHostResponse Resize(int Width,int Height){
        this.EnsureReady();
        return this.Send(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=this.SessionId,
            Sequence=++this.Sequence,
            Command=RenderPreviewHostCommand.Resize,
            Width=Width,
            Height=Height,
        });
    }

    internal RenderPreviewHostResponse UpdateFrame(RenderPreviewFrame Frame){
        this.EnsureReady();
        return this.Send(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=this.SessionId,
            Sequence=++this.Sequence,
            Command=RenderPreviewHostCommand.UpdateFrame,
            Frame=Frame,
        });
    }

    internal RenderPreviewHostResponse Render(bool captureForVerification){
        this.EnsureReady();
        return this.Send(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=this.SessionId,
            Sequence=++this.Sequence,
            Command=RenderPreviewHostCommand.Render,
            CaptureForVerification=captureForVerification,
        });
    }

    internal RenderPreviewDebugSnapshot GetDebugSnapshot(){
        this.EnsureReady();
        RenderPreviewHostResponse Response=this.Send(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),
            SessionId=this.SessionId,
            Sequence=++this.Sequence,
            Command=RenderPreviewHostCommand.Snapshot,
        },responseFailureIsTerminal:false);
        RenderPreviewDebugSnapshot?Snapshot=Response.DebugSnapshot;
        string?ValidationError;
        try {
            ValidationError=RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot);
            if(ValidationError is null&&Snapshot is not null
                &&(Snapshot.DeviceGeneration!=Response.DeviceGeneration
                    ||Snapshot.CompletedFence!=Response.CompletedFence)){
                ValidationError=
                    "Render debug snapshot generation or fence does not match its response envelope.";
            }
        } catch(Exception Error){
            this.Fault();
            throw new RenderPreviewHostException(
                $"Preview host debug snapshot validation failed: {Error.Message}",
                RequiresFreshSurfaceGeneration:true,Error);
        }
        if(ValidationError is not null){
            this.Fault();
            throw new RenderPreviewHostException(
                $"Preview host returned an invalid debug snapshot: {ValidationError}",
                RequiresFreshSurfaceGeneration:true);
        }
        return Snapshot!;
    }

    internal void Close(){
        if(this.State==RenderPreviewHostClientState.Disposed){ return; }
        if(this.State==RenderPreviewHostClientState.Ready){
            try {
                this.Send(new RenderPreviewHostRequest {
                    RequestId=Guid.NewGuid(),
                    SessionId=this.SessionId,
                    Sequence=++this.Sequence,
                    Command=RenderPreviewHostCommand.Close,
                });
            } catch(RenderPreviewHostException){
                // Terminal state is already recorded. Disposal must still reclaim the child.
            }
        }
        this.State=RenderPreviewHostClientState.Disposed;
        this.ReleaseTransport();
    }

    public void Dispose()=>this.Close();

    private RenderPreviewHostResponse Send(
        RenderPreviewHostRequest Request,bool responseFailureIsTerminal=true){
        try {
            RenderPreviewHostResponse Response=this.SendRequest(Request)
                ??throw new InvalidOperationException("Preview host returned no response.");
            if(Response.ProtocolVersion!=RenderPreviewHostProtocol.CurrentVersion
                ||Response.RequestId!=Request.RequestId
                ||Response.SessionId!=Request.SessionId
                ||Response.Sequence!=Request.Sequence){
                throw new RenderPreviewHostException(
                    "Preview host response correlation failed.",
                    RequiresFreshSurfaceGeneration:true);
            }
            if(Response.State!=RenderPreviewHostResultState.Succeeded){
                bool Fresh=Response.State is RenderPreviewHostResultState.HostCrashed;
                bool Terminal=responseFailureIsTerminal||Fresh
                    ||Response.State is not RenderPreviewHostResultState.Failed;
                throw new RenderPreviewHostException(
                    $"{Response.DiagnosticCode}: {Response.Message}",Fresh,
                    InnerException:null,SessionTerminated:Terminal);
            }
            return Response;
        } catch(RenderPreviewHostException Error){
            if(Error.SessionTerminated){ this.Fault(); }
            throw;
        } catch(Exception Error){
            this.Fault();
            throw new RenderPreviewHostException(
                $"Preview host transport failed: {Error.Message}",
                RequiresFreshSurfaceGeneration:true,Error);
        }
    }

    private void EnsureReady(){
        if(this.State!=RenderPreviewHostClientState.Ready){
            throw new InvalidOperationException($"Preview client is not ready: {this.State}.");
        }
    }

    private void Fault(){
        this.State=RenderPreviewHostClientState.Faulted;
        this.ReleaseTransport();
    }

    private void ReleaseTransport(){
        if(this.TransportDisposed){ return; }
        this.TransportDisposed=true;
        this.DisposeTransport();
    }
}

internal static class PreviewHostProcessStartInfo {
    internal static ProcessStartInfo Create(
        string HostPath,string HostArguments,string?WorkingDirectory=null,
        int?ParentProcessId=null){
        if(string.IsNullOrWhiteSpace(HostPath)||!File.Exists(HostPath)){
            throw new FileNotFoundException("Render Preview Host was not found.",HostPath);
        }
        int OwnerProcessId;
        if(ParentProcessId is { }ExplicitOwner){
            OwnerProcessId=ExplicitOwner;
        }else{
            using Process CurrentProcess=Process.GetCurrentProcess();
            OwnerProcessId=CurrentProcess.Id;
        }
        if(OwnerProcessId<=0){
            throw new ArgumentOutOfRangeException(nameof(ParentProcessId));
        }
        string FullPath=Path.GetFullPath(HostPath);
        bool IsDll=string.Equals(
            Path.GetExtension(FullPath),".dll",StringComparison.OrdinalIgnoreCase);
        string OwnedArguments=$"{HostArguments} --parent-process-id {OwnerProcessId}";
        return new ProcessStartInfo(
            IsDll?"dotnet":FullPath,
            IsDll?$"{Quote(FullPath)} {OwnedArguments}":OwnedArguments){
            UseShellExecute=false,
            CreateNoWindow=true,
            WorkingDirectory=string.IsNullOrWhiteSpace(WorkingDirectory)
                ?Path.GetDirectoryName(FullPath)??Environment.CurrentDirectory
                :Path.GetFullPath(WorkingDirectory),
        };
    }

    internal static string Quote(string Value)=>
        string.Concat((char)34,Value,(char)34);
}

internal sealed class RenderPreviewHostProcessTransport:IDisposable {
    private readonly string PipeName=$"CSharpGameEngine.Preview.{Guid.NewGuid():N}";
    private readonly Process Process;
    private bool Disposed;
    private bool ConnectedOnce;
    private readonly TimeSpan ResponseTimeout;

    internal RenderPreviewHostProcessTransport(string HostPath,TimeSpan ResponseTimeout){
        this.ResponseTimeout=ResponseTimeout;
        ProcessStartInfo StartInfo=PreviewHostProcessStartInfo.Create(
            HostPath,$"--render-server {PreviewHostProcessStartInfo.Quote(this.PipeName)}");
        this.Process=Process.Start(StartInfo)
            ??throw new InvalidOperationException("Render Preview Host did not start.");
    }

    internal RenderPreviewHostResponse Send(RenderPreviewHostRequest Request){
        if(this.Disposed){ throw new ObjectDisposedException(nameof(RenderPreviewHostProcessTransport)); }
        using NamedPipeClientStream Pipe=new(
            ".",this.PipeName,PipeDirection.InOut,PipeOptions.None);
        bool Snapshot=Request.Command==RenderPreviewHostCommand.Snapshot;
        Connect(Pipe,this.Process,Snapshot
            ?TimeSpan.FromSeconds(1)
            :this.ConnectedOnce?TimeSpan.FromSeconds(2):TimeSpan.FromSeconds(10));
        this.ConnectedOnce=true;
        ResidentBridgeClient.WriteMessage(Pipe,Request);
        return ResidentBridgeClient.ReadMessage<RenderPreviewHostResponse>(
            Pipe,Snapshot?TimeSpan.FromSeconds(2):this.ResponseTimeout,
            CancellationToken.None);
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        try {
            if(!this.Process.HasExited&&!this.Process.WaitForExit(1_000)){
                this.Process.Kill();
                this.Process.WaitForExit(1_000);
            }
        } catch(InvalidOperationException){ }
        finally { this.Process.Dispose(); }
    }

    private static void Connect(NamedPipeClientStream Pipe,Process Process,TimeSpan Timeout){
        DateTime Deadline=DateTime.UtcNow+Timeout;
        while(true){
            if(Process.HasExited){
                throw new IOException($"Render Preview Host exited with code {Process.ExitCode}.");
            }
            try {
                Pipe.Connect(100);
                return;
            } catch(TimeoutException)when(DateTime.UtcNow<Deadline){
                Thread.Sleep(25);
            }
        }
    }
}
