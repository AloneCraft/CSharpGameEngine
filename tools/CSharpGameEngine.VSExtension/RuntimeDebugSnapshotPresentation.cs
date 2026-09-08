using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Immutable, bounded UI projection of one explicitly requested Render Server snapshot. It never
/// polls, retains a runtime handle, or converts the declared graph into fictitious execution data.
/// </summary>
internal sealed class RuntimeDebugSnapshotPresentation {
    private RuntimeDebugSnapshotPresentation(string Summary,string[]Rows){
        this.Summary=Summary;
        this.Rows=Array.AsReadOnly(Rows);
    }

    internal string Summary{get;}
    internal IReadOnlyList<string>Rows{get;}

    internal static RuntimeDebugSnapshotPresentation Create(
        RenderPreviewDebugSnapshot Snapshot){
        string?Error=RenderPreviewHostProtocol.ValidateDebugSnapshot(Snapshot);
        if(Error is not null){ throw new ArgumentException(Error,nameof(Snapshot)); }

        string GraphKind=Snapshot.GraphKind==RenderPreviewDebugGraphKind.DeclaredPlan
            ?VsixText.Get("RuntimeDebugGraphDeclared")
            :Snapshot.GraphKind.ToString();
        string Summary=VsixText.Format(
            "RuntimeDebugSummary",Snapshot.DeviceState,Snapshot.DeviceGeneration,
            Snapshot.CompletedFence,Snapshot.FramesRendered,Snapshot.SceneObjectCount,
            Snapshot.PrimaryObjectDrawCount,Snapshot.ResidentMaterialTextureCount,
            Snapshot.ErrorOrWorseMessageCount,Snapshot.AdapterDescription,
            Snapshot.IsWarp,GraphKind);
        List<string>Rows=[
            VsixText.Format(
                "RuntimeDebugPlatformSummary",Snapshot.Platform.OsDescription,
                Snapshot.Platform.OsArchitecture,Snapshot.Platform.ProcessArchitecture,
                Snapshot.Platform.RuntimeDescription,Snapshot.Platform.ProcessId,
                Snapshot.Platform.CapturedCounter,Snapshot.Platform.CounterFrequency),
            VsixText.Format(
                "RuntimeDebugInputSummary",Snapshot.Platform.Input.State,
                Snapshot.Platform.Input.ActiveContextCount,
                Snapshot.Platform.Input.HeldControlCount,
                Snapshot.Platform.Input.ActiveActionCount,
                Snapshot.Platform.Input.Actions.Length,
                Snapshot.Platform.Input.ActionsTruncated,
                Detail(Snapshot.Platform.Input.Detail)),
            VsixText.Format(
                "RuntimeDebugAudioSummary",Snapshot.Platform.Audio.State,
                Snapshot.Platform.Audio.Mode,Snapshot.Platform.Audio.Generation,
                Snapshot.Platform.Audio.ActiveVoiceCount,
                Snapshot.Platform.Audio.MaximumVoiceCount,
                Snapshot.Platform.Audio.BusCount,
                Snapshot.Platform.Audio.RestoredVoiceCount,
                Snapshot.Platform.Audio.StoppedVoiceCount,
                Snapshot.Platform.Audio.RecoveryEventCount,
                Snapshot.Platform.Audio.DroppedRecoveryEventCount,
                Detail(Snapshot.Platform.Audio.LastFailure),
                Detail(Snapshot.Platform.Audio.Detail)),
            VsixText.Format(
                "RuntimeDebugGraphSummary",Snapshot.QualityProfile,
                Snapshot.Passes.Length,Snapshot.Resources.Length,
                Snapshot.Transitions.Length,Snapshot.SurfaceWidth,Snapshot.SurfaceHeight),
            VsixText.Format(
                "RuntimeDebugDiagnosticSummary",Snapshot.Diagnostics.Length,
                Snapshot.DiagnosticsTruncated),
        ];
        foreach(PlatformDebugInputAction Action in Snapshot.Platform.Input.Actions){
            Rows.Add(VsixText.Format(
                "RuntimeDebugInputAction",Action.TimestampTicks,Action.WindowId,
                Action.DeviceId,Action.Sequence,Action.ContextId,Action.ActionId,
                Action.Phase,Action.RawX,Action.RawY,Action.ValueX,Action.ValueY,
                Action.ReleaseReason));
        }
        foreach(RenderPreviewDebugPass Pass in Snapshot.Passes
            .OrderBy(static Value=>Value.ExecutionIndex)){
            Rows.Add(VsixText.Format(
                "RuntimeDebugPass",Pass.ExecutionIndex,Pass.Index,Pass.Name,Pass.Queue,
                JoinIndices(Pass.Dependencies)));
        }
        foreach(RenderPreviewDebugResource Resource in Snapshot.Resources
            .OrderBy(static Value=>Value.Index)){
            string Shape=Resource.Kind==RenderPreviewDebugResourceKind.Buffer
                ?VsixText.Format("RuntimeDebugBufferShape",Resource.ByteSize)
                :VsixText.Format(
                    "RuntimeDebugTextureShape",Resource.Width,Resource.Height,
                    Resource.Format);
            Rows.Add(VsixText.Format(
                "RuntimeDebugResource",Resource.Index,Resource.Name,Resource.Kind,
                Shape,Resource.Imported,Resource.FirstUse,Resource.LastUse,
                Resource.AliasSlot));
        }
        foreach(RenderPreviewDebugTransition Transition in Snapshot.Transitions){
            Rows.Add(VsixText.Format(
                "RuntimeDebugTransition",Transition.PassIndex,
                Transition.ResourceIndex,Transition.Before,Transition.After,
                Transition.SourceQueue,Transition.DestinationQueue,
                Transition.QueueOwnershipTransfer));
        }
        if(Snapshot.Diagnostics.Length==0){
            Rows.Add(VsixText.Get("RuntimeDebugNoDiagnostics"));
        } else {
            foreach(RenderPreviewDebugDiagnostic Diagnostic in Snapshot.Diagnostics){
                Rows.Add(VsixText.Format(
                    "RuntimeDebugDiagnostic",Diagnostic.Severity,
                    Diagnostic.Category,Diagnostic.Id,Diagnostic.Description));
            }
        }
        return new RuntimeDebugSnapshotPresentation(Summary,[..Rows]);
    }

    private static string JoinIndices(int[]Values)=>Values.Length==0
        ?VsixText.Get("RuntimeDebugNoDependencies")
        :string.Join(",",Values.Select(static Value=>
            Value.ToString(CultureInfo.InvariantCulture)));

    private static string Detail(string?Value)=>string.IsNullOrWhiteSpace(Value)
        ?VsixText.Get("RuntimeDebugNoDetail"):Value!;
}
