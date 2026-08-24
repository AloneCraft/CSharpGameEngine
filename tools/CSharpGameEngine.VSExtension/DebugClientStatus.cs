using System;
namespace CSharpGameEngine.VSExtension;
internal enum DebugStatusReason {
    None,
    Connected,
    Disconnected,
    PipeUnavailable,
    AccessDenied,
    InvalidResponse,
    SnapshotUnavailable,
    RemoteRejected
}
internal readonly record struct DebugClientStatus(DebugStatusReason Reason,string? WireToken) {
    internal static DebugClientStatus FromWireToken(string? WireToken,DebugStatusReason UnsuccessfulReason){
        if(string.IsNullOrEmpty(WireToken)){
            return TransportFailure(DebugStatusReason.InvalidResponse);
        }
        return new DebugClientStatus(
            string.Equals(WireToken,"ok",StringComparison.Ordinal)?DebugStatusReason.Connected:UnsuccessfulReason,
            WireToken);
    }
    internal static DebugClientStatus TransportFailure(DebugStatusReason Reason)=>new(Reason,null);
}
