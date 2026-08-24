using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsDebugBody(
    int EntityId,ulong StableId,PhysicsAabb Bounds,PhysicsVector3 Position,
    PhysicsVector3 LinearVelocity,bool IsSleeping,PhysicsMotionType? MotionType);

public sealed class PhysicsDebugSnapshot {
    public Guid WorldId { get; }
    public ulong Tick { get; }
    public IReadOnlyList<PhysicsDebugBody> Bodies { get; }
    public IReadOnlyList<PhysicsEvent> Events { get; }
    internal PhysicsDebugSnapshot(Guid WorldId,ulong Tick,PhysicsDebugBody[] Bodies,PhysicsEvent[] Events){
        this.WorldId=WorldId;this.Tick=Tick;this.Bodies=Bodies;this.Events=Events;
    }
}

internal static class PhysicsDebugHub {
    private const int ProtocolVersion=1;
    private const int MaximumBodies=8192;
    private static readonly ConcurrentDictionary<Guid,PhysicsDebugSnapshot> Snapshots=[];
    private static int Started;
    private static int SnapshotRequested;

    internal static string PipeName=>$"CSharpGameEngine.PhysicsDebug.{Environment.ProcessId}";

    internal static void EnsureStarted(){
        if(Interlocked.Exchange(ref Started,1)!=0){ return; }
        _=Task.Run(ServerLoopAsync);
    }

    internal static void PublishIfRequested(Guid WorldId,DeterministicPhysicsWorld World,ulong Tick){
        if(Interlocked.Exchange(ref SnapshotRequested,0)==0){ return; }
        List<PhysicsDebugBody> Bodies=[];
        World.CollectDebugBodies(Bodies,MaximumBodies);
        PhysicsEvent[] Events=[.. World.Events];
        Snapshots[WorldId]=new PhysicsDebugSnapshot(WorldId,Tick,[.. Bodies],Events);
    }

    private static async Task ServerLoopAsync(){
        while(true){
            try {
                using NamedPipeServerStream Pipe=new(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
                await Pipe.WaitForConnectionAsync().ConfigureAwait(false);
                await HandleAsync(Pipe).ConfigureAwait(false);
            }
            catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or JsonException){
                // Debug transport must never terminate the game.
            }
        }
    }

    private static async Task HandleAsync(Stream Pipe){
        byte[] LengthBytes=new byte[4];
        await Pipe.ReadExactlyAsync(LengthBytes).ConfigureAwait(false);
        int Length=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(LengthBytes);
        if(Length is <=0 or >64*1024){ throw new InvalidDataException(); }
        byte[] Payload=new byte[Length];
        await Pipe.ReadExactlyAsync(Payload).ConfigureAwait(false);
        PhysicsDebugRequest? Request=JsonSerializer.Deserialize<PhysicsDebugRequest>(Payload);
        if(Request?.Protocol!=ProtocolVersion){ throw new InvalidDataException(); }
        PhysicsDebugResponse Response;
        if(Request.Command=="snapshot"){
            Interlocked.Exchange(ref SnapshotRequested,1);
            PhysicsDebugSnapshot? Snapshot=Resolve(Request.WorldId);
            Response=Snapshot is null
                ?new PhysicsDebugResponse { Protocol=ProtocolVersion,Status="pending" }
                :ToResponse(Snapshot);
        }
        else {
            string Status=PhysicsSimulationControl.Apply(Request.Command,Request.WorldId);
            Response=new PhysicsDebugResponse { Protocol=ProtocolVersion,Status=Status };
        }
        byte[] ResponseBytes=JsonSerializer.SerializeToUtf8Bytes(Response);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(LengthBytes,ResponseBytes.Length);
        await Pipe.WriteAsync(LengthBytes).ConfigureAwait(false);
        await Pipe.WriteAsync(ResponseBytes).ConfigureAwait(false);
        await Pipe.FlushAsync().ConfigureAwait(false);
    }

    private static PhysicsDebugSnapshot? Resolve(string? WorldId){
        if(Guid.TryParse(WorldId,out Guid Parsed)&&Snapshots.TryGetValue(Parsed,out PhysicsDebugSnapshot? Selected)){ return Selected; }
        PhysicsDebugSnapshot? Latest=null;
        foreach(PhysicsDebugSnapshot Candidate in Snapshots.Values){
            if(Latest is null||Candidate.Tick>Latest.Tick){ Latest=Candidate; }
        }
        return Latest;
    }

    private static PhysicsDebugResponse ToResponse(PhysicsDebugSnapshot Snapshot)=>new(){
        Protocol=ProtocolVersion,Status="ok",WorldId=Snapshot.WorldId,Tick=Snapshot.Tick,
        Bodies=[.. Snapshot.Bodies.Select(static Body=>new PhysicsDebugBodyDto {
            EntityId=Body.EntityId,StableId=Body.StableId,
            MinR=Body.Bounds.Min.R.ToDouble(),MinU=Body.Bounds.Min.U.ToDouble(),MinF=Body.Bounds.Min.F.ToDouble(),
            MaxR=Body.Bounds.Max.R.ToDouble(),MaxU=Body.Bounds.Max.U.ToDouble(),MaxF=Body.Bounds.Max.F.ToDouble(),
            PositionR=Body.Position.R.ToDouble(),PositionU=Body.Position.U.ToDouble(),PositionF=Body.Position.F.ToDouble(),
            VelocityR=Body.LinearVelocity.R.ToDouble(),VelocityU=Body.LinearVelocity.U.ToDouble(),VelocityF=Body.LinearVelocity.F.ToDouble(),
            IsSleeping=Body.IsSleeping,MotionType=Body.MotionType?.ToString(),
        })],
        Events=[.. Snapshot.Events.Select(static Event=>new PhysicsDebugEventDto {
            Tick=Event.Tick,PairId=Event.PairId,Type=Event.Type.ToString(),FirstEntityId=Event.First.Id,SecondEntityId=Event.Second.Id,
        })],
    };

    private sealed class PhysicsDebugRequest { public int Protocol { get;set; }=ProtocolVersion;public string? Command { get;set; }public string? WorldId { get;set; } }
    private sealed class PhysicsDebugResponse { public int Protocol { get;set; }public string Status { get;set; }="pending";public Guid WorldId { get;set; }public ulong Tick { get;set; }public PhysicsDebugBodyDto[] Bodies { get;set; }=[];public PhysicsDebugEventDto[] Events { get;set; }=[]; }
    private sealed class PhysicsDebugBodyDto { public int EntityId { get;set; }public ulong StableId { get;set; }public double MinR { get;set; }public double MinU { get;set; }public double MinF { get;set; }public double MaxR { get;set; }public double MaxU { get;set; }public double MaxF { get;set; }public double PositionR { get;set; }public double PositionU { get;set; }public double PositionF { get;set; }public double VelocityR { get;set; }public double VelocityU { get;set; }public double VelocityF { get;set; }public bool IsSleeping { get;set; }public string? MotionType { get;set; } }
    private sealed class PhysicsDebugEventDto { public ulong Tick { get;set; }public ulong PairId { get;set; }public string Type { get;set; }="";public int FirstEntityId { get;set; }public int SecondEntityId { get;set; } }
}
