using System;
namespace CSharpGameEngine.VSExtension;
/// <summary>新しい描画面での復旧は一度だけ試し、Defaultへの縮退は読み込み成功後だけ通知する。</summary>
internal sealed class AssetViewportRecovery {
    private bool FreshSurfaceAttempted;
    internal void Reset()=>this.FreshSurfaceAttempted=false;
    internal bool TryRecover(bool RequiresFreshSurface,bool IsDefaultTemplate,Func<bool>ReloadFresh,Func<bool>LoadDefault,Action DefaultApplied){
        if(RequiresFreshSurface){
            if(this.FreshSurfaceAttempted){ return false; }
            this.FreshSurfaceAttempted=true;
            return ReloadFresh();
        }
        if(IsDefaultTemplate||!LoadDefault()){ return false; }
        DefaultApplied();
        return true;
    }
}
