using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace CSharpGameEngine.Entities;
/// <summary>One fixed-point-independent wire node used by the Scene View diagnostics channel.</summary>
public readonly record struct SpatialDebugWireNode(
    float MinX,float MinY,float MinZ,float MaxX,float MaxY,float MaxZ,
    int Depth,int EntryCount,bool IsRoot);
/// <summary>Immutable global spatial snapshot exposed to the opt-in Scene View diagnostics client.</summary>
public sealed class SpatialDebugSnapshot {
    public Guid WorldId { get; }
    public long Sequence { get; }
    public bool Truncated { get; }
    public IReadOnlyList<SpatialDebugWireNode> Nodes { get; }
    internal SpatialDebugSnapshot(Guid WorldId,long Sequence,bool Truncated,SpatialDebugWireNode[] Nodes){
        this.WorldId=WorldId;
        this.Sequence=Sequence;
        this.Truncated=Truncated;
        this.Nodes=Nodes;
    }
}
internal static class SpatialDebugHub {
    internal const int ProtocolVersion=1;
    internal const int MaxWireNodes=8192;
    private static readonly ConcurrentDictionary<Guid,SpatialDebugSnapshot>Snapshots=[];
    private static readonly ConcurrentDictionary<Guid,EntityLooseOctree>Indexes=[];
    private static readonly object StartGate=new();
    private static long LastRequestTicks;
    private static int Started;
    private static long Sequence;
    /// <summary>
    /// The world a request without a world id gets when several publish in one process (M0096: the Scene view's
    /// render server holds the view world and the editor world, and the overlay shows the editor world).
    /// </summary>
    private static Guid PreferredWorldId;
    internal static string PipeName=>$"CSharpGameEngine.SpatialDebug.{Environment.ProcessId}";
    internal static void Prefer(Guid WorldId)=>PreferredWorldId=WorldId;
    internal static void Publish(Guid WorldId,EntityLooseOctree Index){
        EnsureStarted();
        Indexes[WorldId]=Index;
        if(IsRequested()){
            Capture(WorldId,Index);
        }
    }
    private static void Capture(Guid WorldId,EntityLooseOctree Index){
        List<SpatialDebugNode>Nodes=[];
        Index.CollectDebugNodes(Nodes);
        bool Truncated=Nodes.Count>MaxWireNodes;
        int Count=Math.Min(Nodes.Count,MaxWireNodes);
        List<SpatialDebugWireNode>WireNodes=new(Count);
        for(int IndexValue=0;IndexValue<Count;IndexValue++){
            SpatialAabb Bounds=Nodes[IndexValue].Bounds;
            if(!Bounds.Min.TryToSingle(out var Min)||!Bounds.Max.TryToSingle(out var Max)){
                Truncated=true;
                continue;
            }
            WireNodes.Add(new SpatialDebugWireNode(
                Min.X,Min.Y,Min.Z,Max.X,Max.Y,Max.Z,
                Nodes[IndexValue].Depth,Nodes[IndexValue].EntryCount,Nodes[IndexValue].IsRoot));
        }
        long CurrentSequence=Interlocked.Increment(ref Sequence);
        Snapshots[WorldId]=new SpatialDebugSnapshot(WorldId,CurrentSequence,Truncated,[.. WireNodes]);
    }
    private static void CaptureRequestedSnapshots(string?WorldId){
        if(Guid.TryParse(WorldId,out Guid Parsed)&&Indexes.TryGetValue(Parsed,out EntityLooseOctree?Selected)){
            Capture(Parsed,Selected);
            return;
        }
        if(WorldId is null&&Indexes.TryGetValue(PreferredWorldId,out EntityLooseOctree?Preferred)){
            Capture(PreferredWorldId,Preferred);
            return;
        }
        foreach(KeyValuePair<Guid,EntityLooseOctree>Pair in Indexes){
            Capture(Pair.Key,Pair.Value);
        }
    }
    private static bool IsRequested()=>DateTime.UtcNow.Ticks-Interlocked.Read(ref LastRequestTicks)<TimeSpan.FromSeconds(2).Ticks;
    internal static void EnsureStarted(){
        if(Interlocked.CompareExchange(ref Started,1,0)!=0){
            return;
        }
        lock(StartGate){
            if(Started!=1){
                return;
            }
            _=Task.Run(ServeAsync);
        }
    }
    private static async Task ServeAsync(){
        while(true){
            NamedPipeServerStream Pipe=new(PipeName,PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            try {
                await Pipe.WaitForConnectionAsync().ConfigureAwait(false);
                _=HandleAsync(Pipe);
            } catch {
                Pipe.Dispose();
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
    }
    private static async Task HandleAsync(NamedPipeServerStream Pipe){
        await using(Pipe){
            try {
                byte[]LengthBytes=await ReadExactlyAsync(Pipe,4).ConfigureAwait(false);
                int Length=BitConverter.ToInt32(LengthBytes,0);
                if(Length<=0||Length>1024*1024){
                    return;
                }
                byte[]Payload=await ReadExactlyAsync(Pipe,Length).ConfigureAwait(false);
                SpatialDebugRequest?Request=JsonSerializer.Deserialize<SpatialDebugRequest>(Payload);
                if(Request is null||!string.Equals(Request.Command,"snapshot",StringComparison.Ordinal)){
                    return;
                }
                Interlocked.Exchange(ref LastRequestTicks,DateTime.UtcNow.Ticks);
                CaptureRequestedSnapshots(Request.WorldId);
                SpatialDebugSnapshot?Snapshot=ResolveSnapshot(Request.WorldId);
                SpatialDebugResponse Response=Snapshot is null
                    ?new SpatialDebugResponse { Protocol=ProtocolVersion,Status="unavailable",WorldId=Guid.Empty,Sequence=0,Truncated=false,Nodes=[] }
                    :new SpatialDebugResponse { Protocol=ProtocolVersion,Status="ok",WorldId=Snapshot.WorldId,Sequence=Snapshot.Sequence,Truncated=Snapshot.Truncated,Nodes=[.. Snapshot.Nodes] };
                byte[]ResponsePayload=JsonSerializer.SerializeToUtf8Bytes(Response);
                await Pipe.WriteAsync(BitConverter.GetBytes(ResponsePayload.Length),0,4).ConfigureAwait(false);
                await Pipe.WriteAsync(ResponsePayload,0,ResponsePayload.Length).ConfigureAwait(false);
                await Pipe.FlushAsync().ConfigureAwait(false);
            } catch {
            }
        }
    }
    private static SpatialDebugSnapshot?ResolveSnapshot(string?WorldId){
        if(Guid.TryParse(WorldId,out Guid Parsed)&&Snapshots.TryGetValue(Parsed,out SpatialDebugSnapshot?Selected)){
            return Selected;
        }
        if(WorldId is null&&Snapshots.TryGetValue(PreferredWorldId,out SpatialDebugSnapshot?Preferred)){
            return Preferred;
        }
        SpatialDebugSnapshot?Latest=null;
        foreach(SpatialDebugSnapshot Candidate in Snapshots.Values){
            if(Latest is null||Candidate.Sequence>Latest.Sequence){
                Latest=Candidate;
            }
        }
        return Latest;
    }
    private static async Task<byte[]>ReadExactlyAsync(Stream Stream,int Count){
        byte[]Buffer=new byte[Count];
        int Offset=0;
        while(Offset<Count){
            int Read=await Stream.ReadAsync(Buffer,Offset,Count-Offset).ConfigureAwait(false);
            if(Read==0){
                throw new EndOfStreamException();
            }
            Offset+=Read;
        }
        return Buffer;
    }
    private sealed class SpatialDebugRequest {
        public string Command { get; set; }="";
        public string? WorldId { get; set; }
    }
    private sealed class SpatialDebugResponse {
        public int Protocol { get; set; }
        public string Status { get; set; }="";
        public Guid WorldId { get; set; }
        public long Sequence { get; set; }
        public bool Truncated { get; set; }
        public SpatialDebugWireNode[] Nodes { get; set; }=[];
    }
}
