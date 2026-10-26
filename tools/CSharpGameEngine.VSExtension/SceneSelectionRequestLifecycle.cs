namespace CSharpGameEngine.VSExtension;
/// <summary>UI スレッド専用の lease。最新の Scene 選択要求だけが結果を公開できる。</summary>
internal sealed class SceneSelectionRequestLifecycle {
    private long NextEpoch;
    private long ActiveEpoch;
    internal bool IsPending=>this.ActiveEpoch!=0;
    internal long Begin(){
        long Epoch=unchecked(++this.NextEpoch);
        if(Epoch==0){
            Epoch=unchecked(++this.NextEpoch);
        }
        this.ActiveEpoch=Epoch;
        return Epoch;
    }
    internal bool IsCurrent(long Epoch)=>Epoch!=0&&this.ActiveEpoch==Epoch;
    internal bool TryComplete(long Epoch){
        if(!this.IsCurrent(Epoch)){
            return false;
        }
        this.ActiveEpoch=0;
        return true;
    }
    internal void Invalidate()=>this.ActiveEpoch=0;
}
