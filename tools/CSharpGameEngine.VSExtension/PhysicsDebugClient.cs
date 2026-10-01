using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

internal readonly record struct ScenePhysicsDebugBody(
    int EntityId,ulong StableId,Vector3 Min,Vector3 Max,Vector3 Position,
    Vector3 Velocity,bool IsSleeping,string MotionType);
internal readonly record struct ScenePhysicsDebugContact(
    ulong PairId,Vector3 Position,Vector3 Normal,float Penetration);
internal readonly record struct ScenePhysicsDebugTreeNode(
    Vector3 Min,Vector3 Max,int Depth,bool IsLeaf,ulong StableId);

internal sealed class ScenePhysicsDebugSnapshot {
    internal Guid WorldId { get; }
    internal ulong Tick { get; }
    internal IReadOnlyList<ScenePhysicsDebugBody> Bodies { get; }
    internal IReadOnlyList<ScenePhysicsDebugContact> Contacts { get; }
    internal IReadOnlyList<ScenePhysicsDebugTreeNode> TreeNodes { get; }
    internal int ExcludedBodyCount { get; }
    internal int ExcludedContactCount { get; }
    internal int ExcludedTreeNodeCount { get; }
    internal ScenePhysicsDebugSnapshot(
        Guid WorldId,ulong Tick,IReadOnlyList<ScenePhysicsDebugBody> Bodies,
        IReadOnlyList<ScenePhysicsDebugContact> Contacts,IReadOnlyList<ScenePhysicsDebugTreeNode> TreeNodes,
        int ExcludedBodyCount,int ExcludedContactCount,int ExcludedTreeNodeCount){
        this.WorldId=WorldId;this.Tick=Tick;this.Bodies=Bodies;this.Contacts=Contacts;this.TreeNodes=TreeNodes;
        this.ExcludedBodyCount=ExcludedBodyCount;this.ExcludedContactCount=ExcludedContactCount;
        this.ExcludedTreeNodeCount=ExcludedTreeNodeCount;
    }
}

internal static class PhysicsDebugClient {
    private const int ProtocolVersion=2;
    private const int MaximumPayloadBytes=16*1024*1024;

    internal static bool TryGetSnapshot(int ProcessId,Guid? WorldId,out ScenePhysicsDebugSnapshot? Snapshot,out DebugClientStatus Status){
        Snapshot=null;
        if(!TryRequest(ProcessId,"snapshot",WorldId,out PhysicsDebugResponse? Response,out Status)){ return false; }
        if(Response is null){
            Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);
            return false;
        }
        Status=DebugClientStatus.FromWireToken(Response.Status,DebugStatusReason.SnapshotUnavailable);
        if(Status.Reason!=DebugStatusReason.Connected){ return false; }
        List<ScenePhysicsDebugBody> Bodies=[];
        int Excluded=0;
        foreach(PhysicsDebugBodyDto Body in Response.Bodies){
            if(!TryConvert(Body,out ScenePhysicsDebugBody Converted)){ Excluded++;continue; }
            Bodies.Add(Converted);
        }
        List<ScenePhysicsDebugContact> Contacts=[];int ExcludedContacts=0;
        foreach(PhysicsDebugContactDto Contact in Response.Contacts){
            if(!TryConvert(Contact,out ScenePhysicsDebugContact Converted)){ ExcludedContacts++;continue; }
            Contacts.Add(Converted);
        }
        List<ScenePhysicsDebugTreeNode> TreeNodes=[];int ExcludedTreeNodes=0;
        foreach(PhysicsDebugTreeNodeDto Node in Response.TreeNodes){
            if(!TryConvert(Node,out ScenePhysicsDebugTreeNode Converted)){ ExcludedTreeNodes++;continue; }
            TreeNodes.Add(Converted);
        }
        Snapshot=new ScenePhysicsDebugSnapshot(
            Response.WorldId,Response.Tick,Bodies,Contacts,TreeNodes,Excluded,ExcludedContacts,ExcludedTreeNodes);

        return true;
    }

    private static bool TryRequest(int ProcessId,string Command,Guid? WorldId,out PhysicsDebugResponse? Response,out DebugClientStatus Status){
        Response=null;Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);
        try {
            using NamedPipeClientStream Pipe=new(".",$"CSharpGameEngine.PhysicsDebug.{ProcessId}",PipeDirection.InOut,PipeOptions.None);
            Pipe.Connect(100);
            byte[] Payload=JsonSerializer.SerializeToUtf8Bytes(new PhysicsDebugRequest { Command=Command,WorldId=WorldId?.ToString() });
            Pipe.Write(BitConverter.GetBytes(Payload.Length),0,4);Pipe.Write(Payload,0,Payload.Length);Pipe.Flush();
            int Length=BitConverter.ToInt32(ReadExactly(Pipe,4),0);
            if(Length is <=0 or >MaximumPayloadBytes){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);return false; }
            Response=JsonSerializer.Deserialize<PhysicsDebugResponse>(ReadExactly(Pipe,Length));
            if(Response is null||Response.Protocol!=ProtocolVersion){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);return false; }
            Status=DebugClientStatus.FromWireToken(Response.Status,DebugStatusReason.RemoteRejected);
            return true;
        }
        catch(TimeoutException){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.PipeUnavailable);return false; }
        catch(IOException){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.PipeUnavailable);return false; }
        catch(UnauthorizedAccessException){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.AccessDenied);return false; }
        catch(InvalidOperationException){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);return false; }
        catch(JsonException){ Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);return false; }
    }

    private static bool TryConvert(PhysicsDebugBodyDto Body,out ScenePhysicsDebugBody Converted){
        Converted=default;
        float[] Values=[Body.MinR,Body.MinU,Body.MinF,Body.MaxR,Body.MaxU,Body.MaxF,Body.PositionR,Body.PositionU,Body.PositionF,Body.VelocityR,Body.VelocityU,Body.VelocityF];
        foreach(float Value in Values){ if(float.IsNaN(Value)||float.IsInfinity(Value)){ return false; } }
        if(Body.MinR>Body.MaxR||Body.MinU>Body.MaxU||Body.MinF>Body.MaxF){ return false; }
        Converted=new ScenePhysicsDebugBody(Body.EntityId,Body.StableId,
            new Vector3(Body.MinR,Body.MinU,Body.MinF),new Vector3(Body.MaxR,Body.MaxU,Body.MaxF),
            new Vector3(Body.PositionR,Body.PositionU,Body.PositionF),new Vector3(Body.VelocityR,Body.VelocityU,Body.VelocityF),
            Body.IsSleeping,Body.MotionType??string.Empty);
        return true;
    }

    private static bool TryConvert(PhysicsDebugContactDto Contact,out ScenePhysicsDebugContact Converted){
        Converted=default;
        float[] Values=[Contact.PositionR,Contact.PositionU,Contact.PositionF,Contact.NormalR,Contact.NormalU,Contact.NormalF,Contact.Penetration];
        foreach(float Value in Values){ if(float.IsNaN(Value)||float.IsInfinity(Value)){ return false; } }
        Vector3 Normal=new(Contact.NormalR,Contact.NormalU,Contact.NormalF);
        if(Contact.Penetration<0f||Normal.LengthSquared()<=1e-10f){ return false; }
        Converted=new ScenePhysicsDebugContact(Contact.PairId,
            new Vector3(Contact.PositionR,Contact.PositionU,Contact.PositionF),Normal,Contact.Penetration);
        return true;
    }

    private static bool TryConvert(PhysicsDebugTreeNodeDto Node,out ScenePhysicsDebugTreeNode Converted){
        Converted=default;
        float[] Values=[Node.MinR,Node.MinU,Node.MinF,Node.MaxR,Node.MaxU,Node.MaxF];
        foreach(float Value in Values){ if(float.IsNaN(Value)||float.IsInfinity(Value)){ return false; } }
        if(Node.MinR>Node.MaxR||Node.MinU>Node.MaxU||Node.MinF>Node.MaxF||Node.Depth is <0 or >63){ return false; }
        Converted=new ScenePhysicsDebugTreeNode(
            new Vector3(Node.MinR,Node.MinU,Node.MinF),new Vector3(Node.MaxR,Node.MaxU,Node.MaxF),
            Node.Depth,Node.IsLeaf,Node.StableId);
        return true;
    }

    private static byte[] ReadExactly(Stream Stream,int Count){
        byte[] Buffer=new byte[Count];int Offset=0;
        while(Offset<Count){ int Read=Stream.Read(Buffer,Offset,Count-Offset);if(Read==0){ throw new EndOfStreamException(); }Offset+=Read; }
        return Buffer;
    }

    private sealed class PhysicsDebugRequest { public int Protocol { get;set; }=ProtocolVersion;public string Command { get;set; }="";public string? WorldId { get;set; } }
    private sealed class PhysicsDebugResponse { public int Protocol { get;set; }public string Status { get;set; }="";public Guid WorldId { get;set; }public ulong Tick { get;set; }public PhysicsDebugBodyDto[] Bodies { get;set; }=[];public PhysicsDebugContactDto[] Contacts { get;set; }=[];public PhysicsDebugTreeNodeDto[] TreeNodes { get;set; }=[]; }
    private sealed class PhysicsDebugBodyDto { public int EntityId { get;set; }public ulong StableId { get;set; }public float MinR { get;set; }public float MinU { get;set; }public float MinF { get;set; }public float MaxR { get;set; }public float MaxU { get;set; }public float MaxF { get;set; }public float PositionR { get;set; }public float PositionU { get;set; }public float PositionF { get;set; }public float VelocityR { get;set; }public float VelocityU { get;set; }public float VelocityF { get;set; }public bool IsSleeping { get;set; }public string? MotionType { get;set; } }
    private sealed class PhysicsDebugContactDto { public ulong PairId { get;set; }public float PositionR { get;set; }public float PositionU { get;set; }public float PositionF { get;set; }public float NormalR { get;set; }public float NormalU { get;set; }public float NormalF { get;set; }public float Penetration { get;set; } }
    private sealed class PhysicsDebugTreeNodeDto { public float MinR { get;set; }public float MinU { get;set; }public float MinF { get;set; }public float MaxR { get;set; }public float MaxU { get;set; }public float MaxF { get;set; }public int Depth { get;set; }public bool IsLeaf { get;set; }public ulong StableId { get;set; } }
}
