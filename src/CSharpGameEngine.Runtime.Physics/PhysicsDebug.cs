using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsDebugBody(
    int EntityId,ulong StableId,PhysicsAabb Bounds,LogicalVector3 Position,
    LogicalVector3 LinearVelocity,bool IsSleeping,PhysicsMotionType? MotionType);
public readonly record struct PhysicsDebugTreeNode(
    PhysicsAabb Bounds,int Depth,bool IsLeaf,ulong StableId);

public sealed class PhysicsDebugSnapshot {
    public Guid WorldId { get; }
    public ulong Tick { get; }
    public IReadOnlyList<PhysicsDebugBody> Bodies { get; }
    public IReadOnlyList<PhysicsEvent> Events { get; }
    public IReadOnlyList<PhysicsContactManifold> Contacts { get; }
    public IReadOnlyList<PhysicsDebugTreeNode> TreeNodes { get; }
    internal PhysicsDebugSnapshot(
        Guid WorldId,ulong Tick,PhysicsDebugBody[] Bodies,PhysicsEvent[] Events,
        PhysicsContactManifold[] Contacts,PhysicsDebugTreeNode[] TreeNodes){
        this.WorldId=WorldId;this.Tick=Tick;this.Bodies=Bodies;this.Events=Events;
        this.Contacts=Contacts;this.TreeNodes=TreeNodes;
    }
}

internal static class PhysicsDebugHub {
    private const int ProtocolVersion=2;
    private const int MaximumBodies=8192;
    private const int MaximumContacts=1024;
    private const int MaximumTreeNodes=2048;
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
        List<PhysicsContactManifold>Contacts=[];
        List<PhysicsDebugTreeNode>TreeNodes=[];
        World.CollectDebugBodies(Bodies,MaximumBodies);
        World.CollectContactManifolds(Contacts,MaximumContacts);
        World.CollectBroadphaseDebugNodes(TreeNodes,MaximumTreeNodes);
        PhysicsEvent[] Events=[.. World.Events];
        Snapshots[WorldId]=new PhysicsDebugSnapshot(
            WorldId,Tick,[.. Bodies],Events,[.. Contacts],[.. TreeNodes]);
    }
    internal static void Remove(Guid WorldId)=>Snapshots.TryRemove(WorldId,out _);

    private static async Task ServerLoopAsync(){
        while(true){
            try {
                using NamedPipeServerStream Pipe=new(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
                await Pipe.WaitForConnectionAsync().ConfigureAwait(false);
                await HandleAsync(Pipe).ConfigureAwait(false);
            }
            catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException){
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
            // M0097: the debug pipe only reads; the editor no longer drives the simulation.
            Response=new PhysicsDebugResponse { Protocol=ProtocolVersion,Status="unsupported command" };
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
            MinR=ToSingle(Body.Bounds.Min.R),MinU=ToSingle(Body.Bounds.Min.U),MinF=ToSingle(Body.Bounds.Min.F),
            MaxR=ToSingle(Body.Bounds.Max.R),MaxU=ToSingle(Body.Bounds.Max.U),MaxF=ToSingle(Body.Bounds.Max.F),
            PositionR=ToSingle(Body.Position.R),PositionU=ToSingle(Body.Position.U),PositionF=ToSingle(Body.Position.F),
            VelocityR=ToSingle(Body.LinearVelocity.R),VelocityU=ToSingle(Body.LinearVelocity.U),VelocityF=ToSingle(Body.LinearVelocity.F),
            IsSleeping=Body.IsSleeping,MotionType=Body.MotionType?.ToString(),
        })],
        Events=[.. Snapshot.Events.Select(static Event=>new PhysicsDebugEventDto {
            Tick=Event.Tick,PairId=Event.PairId,Type=Event.Type.ToString(),FirstEntityId=Event.First.Id,SecondEntityId=Event.Second.Id,
        })],
        Contacts=[.. Snapshot.Contacts.SelectMany(static Contact=>Contact.Points.Select(Point=>new PhysicsDebugContactDto {
            PairId=Contact.PairId,
            PositionR=ToSingle(Point.Position.R),PositionU=ToSingle(Point.Position.U),PositionF=ToSingle(Point.Position.F),
            NormalR=ToSingle(Point.Normal.R),NormalU=ToSingle(Point.Normal.U),NormalF=ToSingle(Point.Normal.F),
            Penetration=ToSingle(Point.Penetration),
        }))],
        TreeNodes=[.. Snapshot.TreeNodes.Select(static Node=>new PhysicsDebugTreeNodeDto {
            MinR=ToSingle(Node.Bounds.Min.R),MinU=ToSingle(Node.Bounds.Min.U),MinF=ToSingle(Node.Bounds.Min.F),
            MaxR=ToSingle(Node.Bounds.Max.R),MaxU=ToSingle(Node.Bounds.Max.U),MaxF=ToSingle(Node.Bounds.Max.F),
            Depth=Node.Depth,IsLeaf=Node.IsLeaf,StableId=Node.StableId,
        })],
    };

    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:throw new InvalidDataException("Physics debug value exceeds the float wire contract.");
    private sealed class PhysicsDebugRequest { public int Protocol { get;set; }=ProtocolVersion;public string? Command { get;set; }public string? WorldId { get;set; } }
    private sealed class PhysicsDebugResponse { public int Protocol { get;set; }public string Status { get;set; }="pending";public Guid WorldId { get;set; }public ulong Tick { get;set; }public PhysicsDebugBodyDto[] Bodies { get;set; }=[];public PhysicsDebugEventDto[] Events { get;set; }=[];public PhysicsDebugContactDto[] Contacts { get;set; }=[];public PhysicsDebugTreeNodeDto[] TreeNodes { get;set; }=[]; }
    private sealed class PhysicsDebugBodyDto { public int EntityId { get;set; }public ulong StableId { get;set; }public float MinR { get;set; }public float MinU { get;set; }public float MinF { get;set; }public float MaxR { get;set; }public float MaxU { get;set; }public float MaxF { get;set; }public float PositionR { get;set; }public float PositionU { get;set; }public float PositionF { get;set; }public float VelocityR { get;set; }public float VelocityU { get;set; }public float VelocityF { get;set; }public bool IsSleeping { get;set; }public string? MotionType { get;set; } }
    private sealed class PhysicsDebugEventDto { public ulong Tick { get;set; }public ulong PairId { get;set; }public string Type { get;set; }="";public int FirstEntityId { get;set; }public int SecondEntityId { get;set; } }
    private sealed class PhysicsDebugContactDto { public ulong PairId { get;set; }public float PositionR { get;set; }public float PositionU { get;set; }public float PositionF { get;set; }public float NormalR { get;set; }public float NormalU { get;set; }public float NormalF { get;set; }public float Penetration { get;set; } }
    private sealed class PhysicsDebugTreeNodeDto { public float MinR { get;set; }public float MinU { get;set; }public float MinF { get;set; }public float MaxR { get;set; }public float MaxU { get;set; }public float MaxF { get;set; }public int Depth { get;set; }public bool IsLeaf { get;set; }public ulong StableId { get;set; } }
}
