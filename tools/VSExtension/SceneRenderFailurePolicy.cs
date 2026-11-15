namespace CSharpGameEngine.VSExtension;

/// <summary>失敗したネイティブサーフェスを、WPF 上で表示可能な診断へ置き換える純粋なポリシー。</summary>
internal static class SceneRenderFailurePolicy {
    internal static bool ShouldShowFailure(bool HasFatalRenderFailure,bool IsBuildPending)
        =>HasFatalRenderFailure&&!IsBuildPending;
}

/// <summary>
/// 最新の非同期シーンビルド要求に対する、UI スレッド専用の lease。ビルド対象が変更された後にワーカーが完了することもあるため、この lease をまだ所有する要求だけが完了できる。
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
    /// 実行中のワーカーを待たずにアクティブな要求を無効にする。後から届くコールバックは意図的に無視する。新しい要求があれば、独自の保留状態を維持する。
    /// </summary>
    internal void Invalidate()=>this.ActiveEpoch=0;

    /// <summary>現在の要求に属するコールバックの場合に限り、lease を完了する。</summary>
    internal bool TryComplete(long Epoch){
        if(this.ActiveEpoch==0||this.ActiveEpoch!=Epoch){
            return false;
        }

        this.ActiveEpoch=0;
        return true;
    }
}