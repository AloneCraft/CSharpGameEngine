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

internal sealed class ScenePhysicsDebugSnapshot {
    internal Guid WorldId { get; }
    internal ulong Tick { get; }
    internal IReadOnlyList<ScenePhysicsDebugBody> Bodies { get; }
    internal int ExcludedBodyCount { get; }
    internal ScenePhysicsDebugSnapshot(Guid WorldId,ulong Tick,IReadOnlyList<ScenePhysicsDebugBody> Bodies,int ExcludedBodyCount){
        this.WorldId=WorldId;this.Tick=Tick;this.Bodies=Bodies;this.ExcludedBodyCount=ExcludedBodyCount;
    }
}

internal static class PhysicsDebugClient {
    private const int ProtocolVersion=1;
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
        Snapshot=new ScenePhysicsDebugSnapshot(Response.WorldId,Response.Tick,Bodies,Excluded);

        return true;
    }

    internal static bool TryControl(int ProcessId,string Command,Guid? WorldId,out DebugClientStatus Status){
        if(!TryRequest(ProcessId,Command,WorldId,out _,out Status)){ return false; }
        return Status.Reason==DebugStatusReason.Connected;
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
        double[] Values=[Body.MinR,Body.MinU,Body.MinF,Body.MaxR,Body.MaxU,Body.MaxF,Body.PositionR,Body.PositionU,Body.PositionF,Body.VelocityR,Body.VelocityU,Body.VelocityF];
        foreach(double Value in Values){ if(double.IsNaN(Value)||double.IsInfinity(Value)||Value<float.MinValue||Value>float.MaxValue){ return false; } }
        if(Body.MinR>Body.MaxR||Body.MinU>Body.MaxU||Body.MinF>Body.MaxF){ return false; }
        Converted=new ScenePhysicsDebugBody(Body.EntityId,Body.StableId,
            new Vector3((float)Body.MinR,(float)Body.MinU,(float)Body.MinF),new Vector3((float)Body.MaxR,(float)Body.MaxU,(float)Body.MaxF),
            new Vector3((float)Body.PositionR,(float)Body.PositionU,(float)Body.PositionF),new Vector3((float)Body.VelocityR,(float)Body.VelocityU,(float)Body.VelocityF),
            Body.IsSleeping,Body.MotionType??string.Empty);
        return true;
    }

    private static byte[] ReadExactly(Stream Stream,int Count){
        byte[] Buffer=new byte[Count];int Offset=0;
        while(Offset<Count){ int Read=Stream.Read(Buffer,Offset,Count-Offset);if(Read==0){ throw new EndOfStreamException(); }Offset+=Read; }
        return Buffer;
    }

    private sealed class PhysicsDebugRequest { public int Protocol { get;set; }=ProtocolVersion;public string Command { get;set; }="";public string? WorldId { get;set; } }
    private sealed class PhysicsDebugResponse { public int Protocol { get;set; }public string Status { get;set; }="";public Guid WorldId { get;set; }public ulong Tick { get;set; }public PhysicsDebugBodyDto[] Bodies { get;set; }=[]; }
    private sealed class PhysicsDebugBodyDto { public int EntityId { get;set; }public ulong StableId { get;set; }public double MinR { get;set; }public double MinU { get;set; }public double MinF { get;set; }public double MaxR { get;set; }public double MaxU { get;set; }public double MaxF { get;set; }public double PositionR { get;set; }public double PositionU { get;set; }public double PositionF { get;set; }public double VelocityR { get;set; }public double VelocityU { get;set; }public double VelocityF { get;set; }public bool IsSleeping { get;set; }public string? MotionType { get;set; } }
}
