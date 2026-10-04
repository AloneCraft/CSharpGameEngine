using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.EditorPreview.Host;

public interface IRenderPreviewSession:IDisposable {
    uint DeviceGeneration{get;}
    ulong CompletedFence{get;}
    string LastFrameHash{get;}
    RenderPreviewCameraDiagnostics[]CameraPreviewDiagnostics{get;}
    /// <summary><paramref name="SceneJson"/> is the edited scene's document for a runtime view; other sessions ignore it.</summary>
    void ReplaceScene(RenderPreviewScene Scene,string SceneJson);
    /// <summary>A runtime view that shows the build's structure because the document changed it (ADR-0070 B1).</summary>
    bool BuildRequired=>false;
    /// <summary>The editor world's id on the Spatial debug pipe (M0096); empty for a session without one.</summary>
    Guid EditorSpatialWorldId=>Guid.Empty;
    void UpdateFrame(RenderPreviewFrame Frame);
    void Resize(int Width,int Height);
    void Render(bool CaptureForVerification);
}

/// <summary>
/// Single source of truth for isolated preview-session ownership and command ordering. A command
/// failure disposes the affected native session rather than retaining partially-applied state.
/// </summary>
public sealed class RenderPreviewHostSessionTable:IDisposable {
    private readonly Func<RenderPreviewHostRequest,IRenderPreviewSession>Factory;
    private readonly Dictionary<Guid,Entry>Sessions=[];
    private bool Disposed;

    public RenderPreviewHostSessionTable(
        Func<RenderPreviewHostRequest,IRenderPreviewSession>Factory){
        this.Factory=Factory??throw new ArgumentNullException(nameof(Factory));
    }

    public RenderPreviewHostResponse Handle(RenderPreviewHostRequest Request){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentNullException.ThrowIfNull(Request);
        string?ValidationError=RenderPreviewHostProtocol.Validate(Request);
        if(ValidationError is not null){
            return Failure(
                Request,
                Request.ProtocolVersion==RenderPreviewHostProtocol.CurrentVersion
                    ?RenderPreviewHostResultState.Failed
                    :RenderPreviewHostResultState.VersionMismatch,
                Request.ProtocolVersion==RenderPreviewHostProtocol.CurrentVersion
                    ?"PREVIEW_REQUEST_INVALID"
                    :"PREVIEW_VERSION_MISMATCH",
                ValidationError);
        }

        if(Request.Command==RenderPreviewHostCommand.Open){
            return this.Open(Request);
        }
        if(!this.Sessions.TryGetValue(Request.SessionId,out Entry?Current)){
            return Failure(Request,RenderPreviewHostResultState.SessionNotFound,
                "PREVIEW_SESSION_NOT_FOUND","The preview session does not exist.");
        }
        if(Request.Sequence!=Current.LastSequence+1){
            return Failure(Request,RenderPreviewHostResultState.StaleSequence,
                "PREVIEW_SEQUENCE_INVALID",
                $"Expected sequence {Current.LastSequence+1}, received {Request.Sequence}.",
                Current.Session);
        }

        try {
            switch(Request.Command){
                case RenderPreviewHostCommand.ReplaceScene:
                    Current.Session.ReplaceScene(Request.Scene!,Request.SceneJson);
                    break;
                case RenderPreviewHostCommand.UpdateFrame:
                    Current.Session.UpdateFrame(Request.Frame!);
                    break;
                case RenderPreviewHostCommand.Resize:
                    Current.Session.Resize(Request.Width,Request.Height);
                    break;
                case RenderPreviewHostCommand.Render:
                    Current.Session.Render(Request.CaptureForVerification);
                    break;
                case RenderPreviewHostCommand.Close:
                    uint Generation=Current.Session.DeviceGeneration;
                    ulong Fence=Current.Session.CompletedFence;
                    this.Sessions.Remove(Request.SessionId);
                    Current.Session.Dispose();
                    return Success(Request,Generation,Fence);
                default:
                    throw new InvalidOperationException($"Unsupported preview command {Request.Command}.");
            }
            Current.LastSequence=Request.Sequence;
            return Success(Request,Current.Session);
        } catch(Exception Error){
            this.Sessions.Remove(Request.SessionId);
            try { Current.Session.Dispose(); } catch { }
            return Failure(Request,RenderPreviewHostResultState.Failed,
                "PREVIEW_COMMAND_FAILED",Error.Message);
        }
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        foreach(Entry Entry in this.Sessions.Values){
            try { Entry.Session.Dispose(); } catch { }
        }
        this.Sessions.Clear();
    }

    private RenderPreviewHostResponse Open(RenderPreviewHostRequest Request){
        if(Request.Sequence!=1){
            return Failure(Request,RenderPreviewHostResultState.StaleSequence,
                "PREVIEW_SEQUENCE_INVALID","Open must use sequence 1.");
        }
        if(this.Sessions.ContainsKey(Request.SessionId)){
            return Failure(Request,RenderPreviewHostResultState.Failed,
                "PREVIEW_SESSION_EXISTS","The preview session already exists.");
        }

        IRenderPreviewSession?Session=null;
        try {
            Session=this.Factory(Request)
                ??throw new InvalidOperationException("Preview session factory returned null.");
            this.Sessions.Add(Request.SessionId,new Entry(Session,Request.Sequence));
            return Success(Request,Session);
        } catch(Exception Error){
            try { Session?.Dispose(); } catch { }
            return Failure(Request,RenderPreviewHostResultState.Failed,
                "PREVIEW_OPEN_FAILED",Error.Message);
        }
    }

    private static RenderPreviewHostResponse Success(
        RenderPreviewHostRequest Request,IRenderPreviewSession Session)=>new(){
            RequestId=Request.RequestId,
            SessionId=Request.SessionId,
            Sequence=Request.Sequence,
            State=RenderPreviewHostResultState.Succeeded,
            DeviceGeneration=Session.DeviceGeneration,
            CompletedFence=Session.CompletedFence,
            FrameHash=Session.LastFrameHash,
            CameraPreviews=Session.CameraPreviewDiagnostics,
            BuildRequired=Session.BuildRequired,
            EditorSpatialWorldId=Session.EditorSpatialWorldId,
        };

    private static RenderPreviewHostResponse Success(
        RenderPreviewHostRequest Request,uint Generation,ulong Fence)=>new(){
            RequestId=Request.RequestId,
            SessionId=Request.SessionId,
            Sequence=Request.Sequence,
            State=RenderPreviewHostResultState.Succeeded,
            DeviceGeneration=Generation,
            CompletedFence=Fence,
            FrameHash=string.Empty,
        };

    private static RenderPreviewHostResponse Failure(
        RenderPreviewHostRequest Request,RenderPreviewHostResultState State,
        string Code,string Message,IRenderPreviewSession?Session=null)=>new(){
            RequestId=Request.RequestId,
            SessionId=Request.SessionId,
            Sequence=Request.Sequence,
            State=State,
            DeviceGeneration=Session?.DeviceGeneration??0,
            CompletedFence=Session?.CompletedFence??0,
            FrameHash=Session?.LastFrameHash??string.Empty,
            CameraPreviews=Session?.CameraPreviewDiagnostics??[],
            DiagnosticCode=Code,
            Message=Message??string.Empty,
        };

    private sealed class Entry(IRenderPreviewSession Session,ulong LastSequence) {
        public IRenderPreviewSession Session{get;}=Session;
        public ulong LastSequence{get;set;}=LastSequence;
    }
}
