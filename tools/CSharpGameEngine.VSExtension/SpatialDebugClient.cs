using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using System.Text.Json;
namespace CSharpGameEngine.VSExtension;
internal sealed class SceneSpatialDebugSnapshot {
    internal Guid WorldId { get; }
    internal long Sequence { get; }
    internal bool Truncated { get; }
    internal int ExcludedNodeCount { get; }
    internal IReadOnlyList<SceneSpatialDebugNode> Nodes { get; }
    internal SceneSpatialDebugSnapshot(Guid WorldId,long Sequence,bool Truncated,IReadOnlyList<SceneSpatialDebugNode>Nodes,int ExcludedNodeCount){
        this.WorldId=WorldId;
        this.Sequence=Sequence;
        this.Truncated=Truncated;
        this.Nodes=Nodes;
        this.ExcludedNodeCount=ExcludedNodeCount;
    }
}
internal readonly record struct SceneSpatialDebugNode(Vector3 Min,Vector3 Max,int Depth,int EntryCount,bool IsRoot);
internal static class SpatialDebugClient {
    private const int ProtocolVersion=1;
    private const int MaxPayloadBytes=16*1024*1024;
    internal static bool TryGetSnapshot(int ProcessId,Guid?WorldId,out SceneSpatialDebugSnapshot?Snapshot,out DebugClientStatus Status){
        Snapshot=null;
        Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);
        try {
            using NamedPipeClientStream Pipe=new(".", $"CSharpGameEngine.SpatialDebug.{ProcessId}",PipeDirection.InOut,PipeOptions.None);
            Pipe.Connect(100);
            SpatialDebugRequest Request=new(){ Command="snapshot",WorldId=WorldId?.ToString() };
            byte[]Payload=JsonSerializer.SerializeToUtf8Bytes(Request);
            Pipe.Write(BitConverter.GetBytes(Payload.Length),0,4);
            Pipe.Write(Payload,0,Payload.Length);
            Pipe.Flush();
            byte[]Length=ReadExactly(Pipe,4);
            int Count=BitConverter.ToInt32(Length,0);
            if(Count<=0||Count>MaxPayloadBytes){
                Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);
                return false;
            }
            SpatialDebugResponse?Response=JsonSerializer.Deserialize<SpatialDebugResponse>(ReadExactly(Pipe,Count));
            if(Response is null||Response.Protocol!=ProtocolVersion){
                Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);
                return false;
            }
            if(!string.Equals(Response.Status,"ok",StringComparison.Ordinal)){
                Status=DebugClientStatus.FromWireToken(Response.Status,DebugStatusReason.SnapshotUnavailable);
                return false;
            }
            List<SceneSpatialDebugNode>Nodes=[];
            int ExcludedNodeCount=0;
            foreach(SpatialDebugWireNode Node in Response.Nodes){
                if(!TryConvert(Node,out SceneSpatialDebugNode Converted)){
                    ExcludedNodeCount++;
                    continue;
                }
                Nodes.Add(Converted);
            }
            Snapshot=new SceneSpatialDebugSnapshot(Response.WorldId,Response.Sequence,Response.Truncated,Nodes,ExcludedNodeCount);
            Status=DebugClientStatus.FromWireToken(Response.Status,DebugStatusReason.SnapshotUnavailable);
            return true;
        } catch(TimeoutException){
            Status=DebugClientStatus.TransportFailure(DebugStatusReason.PipeUnavailable);
            return false;
        } catch(IOException){
            Status=DebugClientStatus.TransportFailure(DebugStatusReason.PipeUnavailable);
            return false;
        } catch(UnauthorizedAccessException){
            Status=DebugClientStatus.TransportFailure(DebugStatusReason.AccessDenied);
            return false;
        } catch(InvalidOperationException){
            Status=DebugClientStatus.TransportFailure(DebugStatusReason.Disconnected);
            return false;
        } catch(JsonException){
            Status=DebugClientStatus.TransportFailure(DebugStatusReason.InvalidResponse);
            return false;
        }
    }
    internal static IReadOnlyList<int>FindProcessIds(string?RuntimeProcessName){
        if(string.IsNullOrWhiteSpace(RuntimeProcessName)){
            return [];
        }
        try {
            Process[]Processes=Process.GetProcessesByName(RuntimeProcessName);
            List<int>ProcessIds=[];
            foreach(Process Process in Processes){
                ProcessIds.Add(Process.Id);
                Process.Dispose();
            }
            ProcessIds.Sort();
            return ProcessIds;
        } catch(ArgumentException){
            return [];
        }
    }
    private static bool TryConvert(SpatialDebugWireNode Node,out SceneSpatialDebugNode Converted){
        Converted=default;
        if(!IsFinite(Node.MinX)||!IsFinite(Node.MinY)||!IsFinite(Node.MinZ)||!IsFinite(Node.MaxX)||!IsFinite(Node.MaxY)||!IsFinite(Node.MaxZ)
            ||Node.MinX<float.MinValue||Node.MinY<float.MinValue||Node.MinZ<float.MinValue
            ||Node.MaxX>float.MaxValue||Node.MaxY>float.MaxValue||Node.MaxZ>float.MaxValue
            ||Node.MinX>float.MaxValue||Node.MinY>float.MaxValue||Node.MinZ>float.MaxValue
            ||Node.MaxX<float.MinValue||Node.MaxY<float.MinValue||Node.MaxZ<float.MinValue
            ||Node.MinX>Node.MaxX||Node.MinY>Node.MaxY||Node.MinZ>Node.MaxZ){
            return false;
        }
        Converted=new SceneSpatialDebugNode(
            new Vector3((float)Node.MinX,(float)Node.MinY,(float)Node.MinZ),
            new Vector3((float)Node.MaxX,(float)Node.MaxY,(float)Node.MaxZ),Node.Depth,Node.EntryCount,Node.IsRoot);
        return true;
    }
    private static bool IsFinite(double Value)=>!double.IsNaN(Value)&&!double.IsInfinity(Value);
    private static byte[]ReadExactly(Stream Stream,int Count){
        byte[]Buffer=new byte[Count];
        int Offset=0;
        while(Offset<Count){
            int Read=Stream.Read(Buffer,Offset,Count-Offset);
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
    private sealed class SpatialDebugWireNode {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        public int Depth { get; set; }
        public int EntryCount { get; set; }
        public bool IsRoot { get; set; }
    }
}