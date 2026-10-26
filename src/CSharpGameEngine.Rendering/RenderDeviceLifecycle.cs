using System.Collections.ObjectModel;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Rendering;

public enum RenderDeviceState:byte {
    Ready,
    Suspended,
    Recovering,
    Faulted,
    Disposed,
}

public enum RenderRecoveryReason:byte {
    Resume,
    DeviceRemoved,
    DeviceReset,
    AdapterChanged,
}

public enum RenderDeviceFailureKind:byte {
    NativeFailure,
    DeviceRemoved,
    DeviceReset,
    ArtifactRebuildFailed,
}

public readonly record struct RenderDeviceFailure(
    RenderDeviceFailureKind Kind,int NativeCode,string Message);

/// <summary>レンダーデバイスの状態遷移1回分の、不変な公開診断情報。ネイティブハンドルやプロバイダー固有のエラーオブジェクトは、この境界を越えない。</summary>
public sealed class RenderDeviceSnapshot {
    private readonly ReadOnlyCollection<AssetArtifactReference>RebuiltArtifactsStorage;

    internal RenderDeviceSnapshot(
        RenderDeviceState State,RenderDeviceState PreviousState,uint Generation,
        ulong CompletedFence,RenderRecoveryReason?LastRecoveryReason,
        RenderDeviceFailure?LastFailure,AssetArtifactReference[]RebuiltArtifacts){
        this.State=State;
        this.PreviousState=PreviousState;
        this.Generation=Generation;
        this.CompletedFence=CompletedFence;
        this.LastRecoveryReason=LastRecoveryReason;
        this.LastFailure=LastFailure;
        this.RebuiltArtifactsStorage=Array.AsReadOnly(RebuiltArtifacts);
    }

    public RenderDeviceState State{get;}
    public RenderDeviceState PreviousState{get;}
    public uint Generation{get;}
    public ulong CompletedFence{get;}
    public RenderRecoveryReason?LastRecoveryReason{get;}
    public RenderDeviceFailure?LastFailure{get;}
    public IReadOnlyList<AssetArtifactReference>RebuiltArtifacts=>this.RebuiltArtifactsStorage;
}

public sealed class RenderDeviceUnavailableException:InvalidOperationException {
    internal RenderDeviceUnavailableException(
        RenderDeviceState State,RenderDeviceFailure?Failure)
        :base(CreateMessage(State,Failure)){
        this.State=State;
        this.Failure=Failure;
    }

    public RenderDeviceState State{get;}
    public RenderDeviceFailure?Failure{get;}

    private static string CreateMessage(
        RenderDeviceState State,RenderDeviceFailure?Failure)=>
        Failure is { }Value
            ?$"The render device is {State}: {Value.Kind} (0x{Value.NativeCode:X8}): {Value.Message}"
            :$"The render device is {State} and cannot accept frame submission.";
}

/// <summary>レンダーデバイスの利用可能状態を示す、プロバイダーに依存しない単一の正本。1つの所有者が自身のレンダースレッド上で状態遷移を進め、ネイティブバックエンドは世代を実装するだけ。</summary>
public sealed class RenderDeviceLifecycle:IDisposable {
    private AssetArtifactReference[]RequiredArtifactArray;
    private ReadOnlyCollection<AssetArtifactReference>RequiredArtifactView;
    private RenderDeviceSnapshot SnapshotValue;

    public RenderDeviceLifecycle(IEnumerable<AssetArtifactReference>RequiredArtifacts){
        ArgumentNullException.ThrowIfNull(RequiredArtifacts);
        this.RequiredArtifactArray=[.. RequiredArtifacts];
        ValidateClosure(this.RequiredArtifactArray,nameof(RequiredArtifacts));
        this.RequiredArtifactView=Array.AsReadOnly(this.RequiredArtifactArray);
        this.SnapshotValue=new RenderDeviceSnapshot(
            RenderDeviceState.Ready,RenderDeviceState.Ready,1,0,null,null,[]);
    }

    public IReadOnlyList<AssetArtifactReference>RequiredArtifacts=>this.RequiredArtifactView;
    public RenderDeviceSnapshot Snapshot=>this.SnapshotValue;

    public void EnsureCanSubmit(){
        this.ThrowIfDisposed();
        if(this.SnapshotValue.State!=RenderDeviceState.Ready){
            throw new RenderDeviceUnavailableException(
                this.SnapshotValue.State,this.SnapshotValue.LastFailure);
        }
    }

    /// <summary>検証済みの新しいアーティファクト一式を原子的に公開する。例として、Debug でのシェーダーのホットリロード成功後に使用する。無効な入力や再構築が不完全な入力で、直近の正常なアーティファクト一式を置き換えることはない。</summary>
    public void ReplaceRequiredArtifacts(
        IEnumerable<AssetArtifactReference>RequiredArtifacts){
        this.ThrowIfDisposed();
        this.RequireState(RenderDeviceState.Ready);
        ArgumentNullException.ThrowIfNull(RequiredArtifacts);
        AssetArtifactReference[]Replacement=[.. RequiredArtifacts];
        ValidateClosure(Replacement,nameof(RequiredArtifacts));
        this.RequiredArtifactArray=Replacement;
        this.RequiredArtifactView=Array.AsReadOnly(Replacement);
    }

    public void Suspend(ulong CompletedFence){
        this.ThrowIfDisposed();
        this.RequireState(RenderDeviceState.Ready);
        this.RequireMonotonicFence(CompletedFence);
        this.Publish(RenderDeviceState.Suspended,CompletedFence,
            this.SnapshotValue.LastRecoveryReason,this.SnapshotValue.LastFailure,
            this.SnapshotValue.RebuiltArtifacts);
    }

    public void BeginRecovery(RenderRecoveryReason Reason,ulong CompletedFence){
        this.ThrowIfDisposed();
        RenderDeviceState Current=this.SnapshotValue.State;
        bool Legal=Current switch {
            RenderDeviceState.Ready=>Reason!=RenderRecoveryReason.Resume,
            RenderDeviceState.Suspended=>Reason==RenderRecoveryReason.Resume,
            _=>false,
        };
        if(!Legal){
            throw new InvalidOperationException(
                $"Cannot begin {Reason} recovery while the render device is {Current}.");
        }
        this.RequireMonotonicFence(CompletedFence);
        this.Publish(RenderDeviceState.Recovering,CompletedFence,Reason,
            this.SnapshotValue.LastFailure,[]);
    }

    public void CompleteRecovery(
        IEnumerable<AssetArtifactReference>RebuiltArtifacts,ulong CompletedFence){
        this.ThrowIfDisposed();
        this.RequireState(RenderDeviceState.Recovering);
        ArgumentNullException.ThrowIfNull(RebuiltArtifacts);
        AssetArtifactReference[]Rebuilt=[.. RebuiltArtifacts];
        ValidateClosure(Rebuilt,nameof(RebuiltArtifacts));
        this.RequireExactClosure(Rebuilt);
        this.RequireMonotonicFence(CompletedFence);
        this.Publish(RenderDeviceState.Ready,CompletedFence,
            this.SnapshotValue.LastRecoveryReason,this.SnapshotValue.LastFailure,
            [.. this.RequiredArtifactArray],checked(this.SnapshotValue.Generation+1));
    }

    public void Fault(RenderDeviceFailure Failure){
        this.ThrowIfDisposed();
        RenderDeviceState Current=this.SnapshotValue.State;
        if(Current is not(RenderDeviceState.Ready or RenderDeviceState.Recovering)){
            throw new InvalidOperationException(
                $"Cannot fault the render device while it is {Current}.");
        }
        if(string.IsNullOrWhiteSpace(Failure.Message)){
            throw new ArgumentException("A render-device failure requires a diagnostic message.",
                nameof(Failure));
        }
        this.Publish(RenderDeviceState.Faulted,this.SnapshotValue.CompletedFence,
            this.SnapshotValue.LastRecoveryReason,Failure,
            this.SnapshotValue.RebuiltArtifacts);
    }

    public void Dispose(){
        if(this.SnapshotValue.State==RenderDeviceState.Disposed){ return; }
        this.Publish(RenderDeviceState.Disposed,this.SnapshotValue.CompletedFence,
            this.SnapshotValue.LastRecoveryReason,this.SnapshotValue.LastFailure,
            this.SnapshotValue.RebuiltArtifacts);
    }

    private void Publish(
        RenderDeviceState State,ulong CompletedFence,
        RenderRecoveryReason?LastRecoveryReason,RenderDeviceFailure?LastFailure,
        IEnumerable<AssetArtifactReference>RebuiltArtifacts,uint?Generation=null){
        RenderDeviceSnapshot Previous=this.SnapshotValue;
        this.SnapshotValue=new RenderDeviceSnapshot(
            State,Previous.State,Generation??Previous.Generation,CompletedFence,
            LastRecoveryReason,LastFailure,[.. RebuiltArtifacts]);
    }

    private void RequireExactClosure(AssetArtifactReference[]Rebuilt){
        if(Rebuilt.Length!=this.RequiredArtifactArray.Length){
            throw new ArgumentException(
                "The rebuilt artifact closure does not contain every required artifact.",
                nameof(Rebuilt));
        }
        HashSet<AssetArtifactReference>Set=[.. Rebuilt];
        foreach(AssetArtifactReference Required in this.RequiredArtifactArray){
            if(!Set.Contains(Required)){
                throw new ArgumentException(
                    $"The rebuilt artifact closure is missing '{Required.Key}'.",
                    nameof(Rebuilt));
            }
        }
    }

    private static void ValidateClosure(
        AssetArtifactReference[]Artifacts,string ParameterName){
        if(Artifacts.Length==0){
            throw new ArgumentException(
                "A render device requires at least one versioned artifact.",ParameterName);
        }
        HashSet<AssetArtifactReference>Seen=[];
        foreach(AssetArtifactReference Artifact in Artifacts){
            if(!Artifact.IsValid){
                throw new ArgumentException(
                    "The render artifact closure contains an invalid identity.",ParameterName);
            }
            if(!Seen.Add(Artifact)){
                throw new ArgumentException(
                    $"The render artifact closure contains duplicate '{Artifact.Key}'.",
                    ParameterName);
            }
        }
    }

    private void RequireState(RenderDeviceState Required){
        if(this.SnapshotValue.State!=Required){
            throw new InvalidOperationException(
                $"The render device must be {Required}, but is {this.SnapshotValue.State}.");
        }
    }

    private void RequireMonotonicFence(ulong CompletedFence){
        if(CompletedFence<this.SnapshotValue.CompletedFence){
            throw new ArgumentOutOfRangeException(nameof(CompletedFence),
                "The completed render fence cannot move backwards.");
        }
    }

    private void ThrowIfDisposed()=>
        ObjectDisposedException.ThrowIf(
            this.SnapshotValue.State==RenderDeviceState.Disposed,this);
}
