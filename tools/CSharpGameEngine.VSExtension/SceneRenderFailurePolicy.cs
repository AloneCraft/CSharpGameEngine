namespace CSharpGameEngine.VSExtension;

/// <summary>Pure policy for replacing a failed native surface with visible WPF diagnostics.</summary>
internal static class SceneRenderFailurePolicy {
    internal static bool ShouldShowFailure(bool HasFatalRenderFailure,bool IsBuildPending)
        =>HasFatalRenderFailure&&!IsBuildPending;
}

/// <summary>
/// UI-thread-confined lease for the latest asynchronous scene-build request. A worker can finish
/// after the target has changed, so only the request that still owns this lease may complete it.
/// </summary>
internal sealed class SceneRenderBuildLifecycle {
    private long NextEpoch;

    internal long ActiveEpoch { get; private set; }

    internal bool IsBuildPending=>this.ActiveEpoch!=0;

    internal long Begin(){
        long Epoch=unchecked(++this.NextEpoch);
        if(Epoch==0){
            Epoch=unchecked(++this.NextEpoch);
        }

        this.ActiveEpoch=Epoch;
        return Epoch;
    }

    /// <summary>
    /// Invalidates the active request without waiting for its worker. Its eventual callback is
    /// intentionally ignored, while a newer request (if any) retains its own pending state.
    /// </summary>
    internal void Invalidate()=>this.ActiveEpoch=0;

    /// <summary>Completes the lease only when the callback belongs to the current request.</summary>
    internal bool TryComplete(long Epoch){
        if(this.ActiveEpoch==0||this.ActiveEpoch!=Epoch){
            return false;
        }

        this.ActiveEpoch=0;
        return true;
    }
}