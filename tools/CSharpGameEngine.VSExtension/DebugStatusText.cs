using System.Globalization;
namespace CSharpGameEngine.VSExtension;
internal static class DebugStatusText {
    internal static string PhysicsProcessUnavailable()=>VsixText.Get("PhysicsDebugProcessUnavailable");
    internal static string Physics(in DebugClientStatus Status)=>Physics(Status,VsixLocalization.UICulture);
    internal static string Physics(in DebugClientStatus Status,CultureInfo Culture)=>
        VsixText.Get(Status.Reason switch {
            DebugStatusReason.Connected=>"PhysicsDebugConnected",
            DebugStatusReason.PipeUnavailable=>"PhysicsDebugPipeUnavailable",
            DebugStatusReason.AccessDenied=>"PhysicsDebugAccessDenied",
            DebugStatusReason.InvalidResponse=>"PhysicsDebugInvalidResponse",
            DebugStatusReason.SnapshotUnavailable=>"PhysicsDebugSnapshotUnavailable",
            DebugStatusReason.RemoteRejected=>"PhysicsDebugRequestRejected",
            _=>"PhysicsDebugDisconnected",
        },Culture);
    internal static string PhysicsRuntimeSnapshot(int ProcessId,ulong Tick,int BodyCount,int ExcludedCount)=>
        VsixText.Format("PhysicsDebugRuntimeSnapshot",ProcessId,Tick,BodyCount,ExcludedCount);
    internal static string PhysicsEditorSnapshot(ulong Tick,int BodyCount,int ExcludedCount)=>
        VsixText.Format("PhysicsDebugEditorSnapshot",Tick,BodyCount,ExcludedCount);
    internal static string Spatial(in DebugClientStatus Status)=>Spatial(Status,VsixLocalization.UICulture);
    internal static string Spatial(in DebugClientStatus Status,CultureInfo Culture)=>
        VsixText.Get(Status.Reason switch {
            DebugStatusReason.PipeUnavailable=>"SceneSpatialPipeUnavailable",
            DebugStatusReason.AccessDenied=>"SceneSpatialAccessDenied",
            DebugStatusReason.InvalidResponse=>"SceneSpatialInvalidResponse",
            DebugStatusReason.SnapshotUnavailable=>"SceneSpatialSnapshotUnavailable",
            DebugStatusReason.RemoteRejected=>"SceneSpatialRequestRejected",
            _=>"SceneSpatialDisconnected",
        },Culture);
    internal static string SpatialRuntimeEmpty(int ProcessId)=>
        VsixText.Format("SceneSpatialRuntimeEmpty",ProcessId);
    internal static string SpatialEditorEmpty()=>VsixText.Get("SceneSpatialEditorEmpty");
    internal static string SpatialRuntimeSnapshot(int ProcessId,int NodeCount,bool Truncated,int ExcludedCount)=>
        VsixText.Format("SceneSpatialRuntimeSnapshot",ProcessId,NodeCount,Truncated,ExcludedCount);
    internal static string SpatialEditorSnapshot(int NodeCount,bool Truncated,int ExcludedCount)=>
        VsixText.Format("SceneSpatialEditorSnapshot",NodeCount,Truncated,ExcludedCount);
}
